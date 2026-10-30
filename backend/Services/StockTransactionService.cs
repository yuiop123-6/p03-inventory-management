using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Data;
using P03.Inventory.Api.Data.Entities;
using P03.Inventory.Api.Dtos;

namespace P03.Inventory.Api.Services;

public interface IStockTransactionService
{
    /// <summary>
    /// 创建库存事务（IN 入库 / OUT 出库 / ADJUST 调整）。
    /// 保证：数据库事务原子性、RequestId 幂等（重放返回首次结果，负载不同 409）、
    /// 数据库条件更新防超卖（E09）。返回 (交易实体, 是否首次创建)。
    /// </summary>
    Task<(StockTransactionDto Dto, bool Created)> CreateAsync(
        CreateStockTransactionRequest request, string actorUserId, CancellationToken ct = default);

    /// <summary>流水分页查询：productId/type/fromUtc(含)/toUtc(不含)，CreatedAt DESC, Id DESC（契约口径）。</summary>
    Task<PagedResult<StockTransactionDto>> QueryAsync(
        long? productId, string? type, DateTime? fromUtc, DateTime? toUtc,
        int page, int pageSize, CancellationToken ct = default);
}

public class StockTransactionService : IStockTransactionService
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const string TypeIn = "IN";
    public const string TypeOut = "OUT";
    public const string TypeAdjust = "ADJUST";

    /// <summary>条件更新（CAS）失败时的最大重试次数，超过则返回 409 STOCK_CONFLICT。</summary>
    private const int MaxCasRetries = 3;

    private static readonly HashSet<string> ValidTypes = new(StringComparer.Ordinal)
    { TypeIn, TypeOut, TypeAdjust };

    // ---- 进程内条带化锁（1024 个信号量池，内存恒定，无泄漏） ----
    // 目的：把“读取库存 → 计算新库存 → 写入”在同进程内串行化，消除读后写竞态窗口；
    // 跨实例/集群部署时由数据库层条件更新（CAS）与 RequestId 唯一索引兜底保证正确性。
    private const int LockStripeCount = 1024;
    private static readonly SemaphoreSlim[] RequestIdLocks =
        Enumerable.Range(0, LockStripeCount).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private static readonly SemaphoreSlim[] ProductLocks =
        Enumerable.Range(0, LockStripeCount).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly AppDbContext _db;

    public StockTransactionService(AppDbContext db) => _db = db;

    public async Task<(StockTransactionDto Dto, bool Created)> CreateAsync(
        CreateStockTransactionRequest request, string actorUserId, CancellationToken ct = default)
    {
        // ---------- 1. 服务端参数校验（控制器模型校验之外的第二道防线，服务可独立调用） ----------
        var requestId = NormalizeRequestId(request.RequestId);
        var type = NormalizeType(request.Type);
        ValidateQuantity(type, request.Quantity);
        var notes = NormalizeNotes(request.Notes);
        var requestHash = ComputeRequestHash(request.ProductId, type, request.Quantity, notes);

        // ---------- 商品信息一次性读取（响应需回显 productCode/productName；不存在 404） ----------
        var productInfo = await _db.Products.AsNoTracking()
            .Where(p => p.Id == request.ProductId)
            .Select(p => new { p.ProductCode, p.Name })
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound($"Product {request.ProductId} does not exist.");

        // ---------- 2. 幂等预检查（快速路径）：相同 RequestId 直接返回首次结果，不重复执行 ----------
        var existing = await FindByRequestIdAsync(requestId, ct);
        if (existing is not null)
            return (VerifyReplay(existing, requestHash, productInfo.ProductCode, productInfo.Name), false);

        // ---------- 3. 双重串行锁：先 RequestId（幂等串行）再 ProductId（库存串行），全程固定顺序无死锁 ----------
        var requestIdLock = RequestIdLocks[(uint)requestId.GetHashCode() % LockStripeCount];
        await requestIdLock.WaitAsync(ct);
        try
        {
            var productIdLock = ProductLocks[(uint)request.ProductId.GetHashCode() % LockStripeCount];
            await productIdLock.WaitAsync(ct);
            try
            {
                // 双重检查：并发同 RequestId 的双重提交可能已插入流水
                existing = await FindByRequestIdAsync(requestId, ct);
                if (existing is not null)
                    return (VerifyReplay(existing, requestHash, productInfo.ProductCode, productInfo.Name), false);

                // ---------- 4. 数据库事务：库存更新 + 流水插入原子提交，禁止部分成功（E05-E08） ----------
                await using var transaction = await _db.Database.BeginTransactionAsync(ct);

                // 引用编号依赖自增 Id，先以唯一占位值插入，保存后回填 TX-{Id:D6}（同事务内完成）
                var transactionRow = new StockTransaction
                {
                    TransactionReference = $"PENDING-{Guid.NewGuid():N}",
                    RequestId = requestId,
                    RequestHash = requestHash,
                    ProductId = request.ProductId,
                    Type = type,
                    Quantity = request.Quantity,
                    Notes = notes,
                    CreatedAt = DateTime.UtcNow,
                    ActorUserId = actorUserId,
                };

                if (_db.Database.IsRelational())
                {
                    // ---- 关系型数据库（SQL Server）：条件更新（CAS）防超卖 ----
                    // WHERE CurrentStock == 读到的值：由数据库行锁保证判断与写入原子完成，
                    // 并发冲突时影响行数为 0 → 重试，超过次数返回 409 STOCK_CONFLICT（E09）
                    var attempt = 0;
                    while (true)
                    {
                        attempt++;

                        var current = await _db.Products.AsNoTracking()
                            .Where(p => p.Id == request.ProductId)
                            .Select(p => (int?)p.CurrentStock)
                            .FirstOrDefaultAsync(ct)
                            ?? throw ApiException.NotFound($"Product {request.ProductId} does not exist.");

                        var newStock = ComputeNewStockOrThrow(type, current, request.Quantity)
                            ?? throw InsufficientStock(request.ProductId, current, request.Quantity);

                        var affected = await _db.Products
                            .Where(p => p.Id == request.ProductId && p.CurrentStock == current)
                            .ExecuteUpdateAsync(u => u.SetProperty(p => p.CurrentStock, newStock), ct);

                        if (affected > 0)
                        {
                            transactionRow.PreviousStock = current;
                            transactionRow.NewStock = newStock;
                            break;
                        }

                        if (attempt >= MaxCasRetries)
                        {
                            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.StockConflict,
                                "Stock was modified by a concurrent operation. Please retry.",
                                new { productId = request.ProductId });
                        }
                    }
                }
                else
                {
                    // ---- InMemory 提供程序（仅单元测试使用）：不支持条件更新 ----
                    // 依赖进程内每商品条带锁串行化后安全的读改写
                    var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, ct)
                        ?? throw ApiException.NotFound($"Product {request.ProductId} does not exist.");

                    var newStock = ComputeNewStockOrThrow(type, product.CurrentStock, request.Quantity)
                        ?? throw InsufficientStock(product.Id, product.CurrentStock, request.Quantity);

                    transactionRow.PreviousStock = product.CurrentStock;
                    transactionRow.NewStock = newStock;
                    product.CurrentStock = newStock;
                }

                // 库存变更与流水同库同事务：一次 SaveChanges 原子落库
                _db.StockTransactions.Add(transactionRow);
                await _db.SaveChangesAsync(ct);

                // 引用编号基于自增 Id 生成（契约示例 TX-000101 风格），同事务内回填
                transactionRow.TransactionReference = $"TX-{transactionRow.Id:D6}";
                await _db.SaveChangesAsync(ct);

                await transaction.CommitAsync(ct);
                return (Map(transactionRow, productInfo.ProductCode, productInfo.Name), true);
            }
            finally
            {
                productIdLock.Release();
            }
        }
        finally
        {
            requestIdLock.Release();
        }
    }

    public async Task<PagedResult<StockTransactionDto>> QueryAsync(
        long? productId, string? type, DateTime? fromUtc, DateTime? toUtc,
        int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var query = _db.StockTransactions.AsNoTracking().AsQueryable();

        if (productId.HasValue)
            query = query.Where(t => t.ProductId == productId.Value);

        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalized = type.Trim().ToUpperInvariant();
            if (!ValidTypes.Contains(normalized))
                throw ApiException.Validation("type must be one of: IN, OUT, ADJUST.", new { type });
            query = query.Where(t => t.Type == normalized);
        }

        if (fromUtc.HasValue)
            query = query.Where(t => t.CreatedAt >= fromUtc.Value); // fromUtc 包含（契约）

        if (toUtc.HasValue)
            query = query.Where(t => t.CreatedAt < toUtc.Value);    // toUtc 不包含（契约）

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(t => t.CreatedAt) // CreatedAt DESC, Id DESC（契约要求）
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            // 关联商品表回显编码/名称（有流水的商品不可删除，INNER JOIN 不丢行）
            .Join(_db.Products.AsNoTracking(),
                t => t.ProductId, p => p.Id,
                (t, p) => new { t, p.ProductCode, ProductName = p.Name })
            .Select(x => Map(x.t, x.ProductCode, x.ProductName))
            .ToListAsync(ct);

        return PagedResult<StockTransactionDto>.Create(items, total, page, pageSize);
    }

    // ---------------- 私有辅助 ----------------

    /// <summary>按 RequestId 查找既有流水（幂等判定）。</summary>
    private Task<StockTransaction?> FindByRequestIdAsync(string requestId, CancellationToken ct)
        => _db.StockTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.RequestId == requestId, ct);

    /// <summary>幂等重放校验：相同负载返回首次结果；相同 RequestId 不同负载 → 409 IDEMPOTENCY_CONFLICT（E13）。</summary>
    private static StockTransactionDto VerifyReplay(
        StockTransaction existing, string requestHash, string productCode, string productName)
    {
        if (existing.RequestHash is not null
            && !string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.IdempotencyConflict,
                "RequestId already used with different payload.",
                new { requestId = existing.RequestId });
        }

        return Map(existing, productCode, productName);
    }

    /// <summary>计算操作后库存。IN 防止 int 溢出；OUT 不足时返回 null（→ 400 INSUFFICIENT_STOCK）。</summary>
    private static int? ComputeNewStockOrThrow(string type, int previousStock, int quantity)
    {
        switch (type)
        {
            case TypeIn:
                // IN：NewStock = PreviousStock + Quantity，防止 int 溢出（需求 04）
                var sum = (long)previousStock + quantity;
                if (sum > int.MaxValue)
                {
                    throw ApiException.Validation(
                        "Resulting stock would exceed the maximum stock level.",
                        new { previousStock, quantity });
                }
                return (int)sum;
            case TypeOut:
                // OUT：仅当 PreviousStock >= Quantity 时生效（需求 04）
                return previousStock >= quantity ? previousStock - quantity : null;
            default:
                // ADJUST：Quantity 即目标新库存，允许 0（需求 04）
                return quantity;
        }
    }

    /// <summary>库存不足业务异常：400 INSUFFICIENT_STOCK（E07）。</summary>
    private static ApiException InsufficientStock(long productId, int currentStock, int requestedQuantity)
        => new(StatusCodes.Status400BadRequest, ErrorCodes.InsufficientStock,
            "Insufficient stock for the requested OUT operation.",
            new { productId, currentStock, requestedQuantity });

    /// <summary>RequestId 必须是 UUID；统一规范化为小写规范形式，保证唯一索引忽略大小写差异。</summary>
    private static string NormalizeRequestId(string? requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId) || !Guid.TryParse(requestId.Trim(), out var guid))
            throw ApiException.Validation("requestId is required and must be a valid UUID.");

        return guid.ToString("D"); // 小写连字符规范形式
    }

    /// <summary>type 只允许 IN / OUT / ADJUST（忽略大小写），规范化为大写存储。</summary>
    private static string NormalizeType(string? type)
    {
        var normalized = type?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!ValidTypes.Contains(normalized))
            throw ApiException.Validation("type must be one of: IN, OUT, ADJUST.", new { type });
        return normalized;
    }

    /// <summary>数量校验：IN/OUT 必须为正整数；ADJUST 为目标库存，允许 0（负数由 DTO Range + 此处双重拦截）。</summary>
    private static void ValidateQuantity(string type, int quantity)
    {
        if (type is TypeIn or TypeOut && quantity <= 0)
            throw ApiException.Validation("quantity must be a positive integer for IN/OUT transactions.",
                new { type, quantity });

        if (type == TypeAdjust && quantity < 0)
            throw ApiException.Validation("quantity must be greater than or equal to 0 for ADJUST transactions.",
                new { type, quantity });
    }

    /// <summary>备注 Trim，空串转 null，超长拒绝。</summary>
    private static string? NormalizeNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return null;
        var trimmed = notes.Trim();
        if (trimmed.Length > 1000)
            throw ApiException.Validation("notes must be at most 1000 characters.");
        return trimmed;
    }

    /// <summary>幂等负载指纹：对规范化后的关键负载做 SHA256，用于识别“相同 RequestId 不同 payload”。</summary>
    private static string ComputeRequestHash(long productId, string type, int quantity, string? notes)
    {
        var canonical = FormattableString.Invariant($"{productId}|{type}|{quantity}|{notes ?? string.Empty}");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes); // 64 位十六进制，匹配 RequestHash nvarchar(64)
    }

    /// <summary>实体 → DTO 映射；时间统一标记 UTC（契约要求 ISO 8601 Z 后缀）。
    /// 商品编码/名称由调用方提供（查询时关联 Products 表）。</summary>
    internal static StockTransactionDto Map(StockTransaction t, string productCode = "", string productName = "")
        => new(
            t.Id,
            t.TransactionReference,
            t.RequestId,
            t.ProductId,
            productCode,
            productName,
            t.Type,
            t.Quantity,
            t.PreviousStock,
            t.NewStock,
            t.Notes,
            DateTime.SpecifyKind(t.CreatedAt, DateTimeKind.Utc));
}
