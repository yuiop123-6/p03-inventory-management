using Microsoft.EntityFrameworkCore;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Data;
using P03.Inventory.Api.Dtos;

namespace P03.Inventory.Api.Services;

public interface IReportService
{
    /// <summary>商品库存报表：categoryId/stockStatus/search 过滤 + 分页 + 库存金额（纯只读）。</summary>
    Task<PagedResult<ReportProductDto>> ProductReportAsync(string? search, long? categoryId, string? stockStatus,
        int page, int pageSize, CancellationToken ct = default);

    /// <summary>历史流水报表：与流水查询同等过滤分页口径，默认最新优先（纯只读）。</summary>
    Task<PagedResult<StockTransactionDto>> TransactionReportAsync(long? productId, string? type,
        DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken ct = default);
}

public class ReportService : IReportService
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private static readonly HashSet<string> ValidStockStatus = new(StringComparer.OrdinalIgnoreCase)
    { StockStatus.All, StockStatus.Normal, StockStatus.Low, StockStatus.Out };

    private readonly AppDbContext _db;
    private readonly IStockTransactionService _stockTransactionService;

    public ReportService(AppDbContext db, IStockTransactionService stockTransactionService)
    {
        _db = db;
        _stockTransactionService = stockTransactionService;
    }

    public async Task<PagedResult<ReportProductDto>> ProductReportAsync(string? search, long? categoryId,
        string? stockStatus, int page, int pageSize, CancellationToken ct = default)
    {
        // ---- 参数校验（与商品查询同口径） ----
        var status = stockStatus?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(status) && !ValidStockStatus.Contains(status))
        {
            throw ApiException.Validation(
                $"stockStatus must be one of: {StockStatus.All}, {StockStatus.Normal}, {StockStatus.Low}, {StockStatus.Out}.",
                new { stockStatus });
        }

        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var query = _db.Products.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // search：编码或名称包含匹配（契约）
            var keyword = search.Trim();
            query = query.Where(p => p.ProductCode.Contains(keyword) || p.Name.Contains(keyword));
        }

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        switch (status)
        {
            case StockStatus.Out:
                query = query.Where(p => p.CurrentStock == 0);
                break;
            case StockStatus.Low:
                query = query.Where(p => p.CurrentStock > 0 && p.CurrentStock <= p.MinimumStockLevel);
                break;
            case StockStatus.Normal:
                query = query.Where(p => p.CurrentStock > p.MinimumStockLevel);
                break;
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(p => p.Id) // 与商品查询同口径：Id 升序稳定分页（E10）
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ReportProductDto(
                p.Id,
                p.ProductCode,
                p.Name,
                p.CategoryId,
                p.Category.Name,
                p.UnitPrice,
                p.CurrentStock,
                p.MinimumStockLevel,
                StockStatus.Of(p.CurrentStock, p.MinimumStockLevel),
                p.UnitPrice * p.CurrentStock)) // 库存金额 = unitPrice × currentStock（契约）
            .ToListAsync(ct);

        return PagedResult<ReportProductDto>.Create(items, total, page, pageSize);
    }

    public Task<PagedResult<StockTransactionDto>> TransactionReportAsync(long? productId, string? type,
        DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken ct = default)
    {
        // 复用库存流水查询口径（过滤、排序 CreatedAt DESC, Id DESC、分页），避免两处实现漂移
        return _stockTransactionService.QueryAsync(productId, type, fromUtc, toUtc, page, pageSize, ct);
    }
}
