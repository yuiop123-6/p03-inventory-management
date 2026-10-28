using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Data;
using P03.Inventory.Api.Data.Entities;
using P03.Inventory.Api.Dtos;
using P03.Inventory.Api.Services;
using Xunit;

namespace P03.Inventory.Tests;

/// <summary>库存事务业务单元测试（成员B）：正常 IN/OUT/ADJUST、库存不足、幂等、并发防超卖、参数校验。</summary>
public class StockTransactionServiceTests
{
    private static (StockTransactionService Service, AppDbContext Db) CreateService()
    {
        var db = CreateDb();
        return (new StockTransactionService(db), db);
    }

    /// <summary>
    /// 本地 InMemory 工厂（不改成员A的 TestHost）：忽略 InMemory 的事务忽略警告，
    /// 使服务内部的 BeginTransaction 事务包装在单元测试中按 no-op 通过。
    /// </summary>
    private static AppDbContext CreateDb(string? dbName = null) => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName ?? $"p03-tests-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>本地便捷建商品：名称默认派生自编码（TestHost.CreateProduct 需显式名称）。</summary>
    private static long CreateProduct(AppDbContext db, long categoryId, string code, int stock = 0, int minStock = 0)
        => TestHost.CreateProduct(db, categoryId, code, $"{code} Name", stock: stock, minStock: minStock);

    private static CreateStockTransactionRequest ValidRequest(
        long productId, string type = "IN", int quantity = 5, string? requestId = null, string? notes = null)
        => new()
        {
            RequestId = requestId ?? Guid.NewGuid().ToString(),
            ProductId = productId,
            Type = type,
            Quantity = quantity,
            Notes = notes,
        };

    /// <summary>并发用例共享同一 InMemory 数据库，每个任务使用独立 DbContext 模拟独立请求。</summary>
    private static Func<AppDbContext> CreateSharedDbFactory()
    {
        var dbName = $"p03-tests-{Guid.NewGuid():N}";
        return () => CreateDb(dbName);
    }

    // ---------------- 正常业务（E05 / E06 / E08） ----------------

    [Fact]
    public async Task Create_IN_IncreasesStock_AndInsertsOneTransaction()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P001", stock: 35);

        var (dto, _) = await service.CreateAsync(ValidRequest(productId, "IN", 5, notes: "补货"), "user-1");

        // 入库：NewStock = PreviousStock + Quantity
        Assert.Equal(35, dto.PreviousStock);
        Assert.Equal(40, dto.NewStock);
        Assert.Equal("IN", dto.Type);
        Assert.Equal(5, dto.Quantity);
        Assert.StartsWith("TX-", dto.TransactionReference);
        Assert.Equal(DateTimeKind.Utc, dto.CreatedAt.Kind);

        // 库存与流水同事务：恰好 1 条流水，商品库存已更新
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.Equal(40, product.CurrentStock);
        Assert.Equal(1, await db.StockTransactions.CountAsync());
    }

    [Fact]
    public async Task Create_OUT_DecreasesStock_AndInsertsOneTransaction()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P002", stock: 10);

        var (dto, _) = await service.CreateAsync(ValidRequest(productId, "OUT", 4), "user-1");

        // 出库：10 → 6（E06）
        Assert.Equal(10, dto.PreviousStock);
        Assert.Equal(6, dto.NewStock);
        Assert.Equal("OUT", dto.Type);

        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.Equal(6, product.CurrentStock);
        Assert.Equal(1, await db.StockTransactions.CountAsync());
    }

    [Fact]
    public async Task Create_ADJUST_SetsAbsoluteTargetStock()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Furniture");
        var productId = CreateProduct(db, categoryId, "P008", stock: 1);

        var (dto, _) = await service.CreateAsync(ValidRequest(productId, "ADJUST", 12), "admin-1");

        // 调整：Quantity 即目标新库存，1 → 12（E08）
        Assert.Equal("ADJUST", dto.Type);
        Assert.Equal(12, dto.Quantity);
        Assert.Equal(1, dto.PreviousStock);
        Assert.Equal(12, dto.NewStock);

        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.Equal(12, product.CurrentStock);
        Assert.Equal(1, await db.StockTransactions.CountAsync());
    }

    [Fact]
    public async Task Create_ADJUST_AllowsZero()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Furniture");
        var productId = CreateProduct(db, categoryId, "P009", stock: 5);

        var (dto, _) = await service.CreateAsync(ValidRequest(productId, "ADJUST", 0), "admin-1");

        Assert.Equal(0, dto.NewStock);
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.Equal(0, product.CurrentStock);
    }

    [Fact]
    public async Task Create_TrimsNotes_AndAcceptsCaseInsensitiveType()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P001", stock: 5);

        var (dto, _) = await service.CreateAsync(
            ValidRequest(productId, "  in  ", 2, notes: "  padded  "), "user-1");

        Assert.Equal("IN", dto.Type);      // 类型规范化为大写
        Assert.Equal("padded", dto.Notes); // 备注 Trim
    }

    // ---------------- 库存不足（E07） ----------------

    [Fact]
    public async Task Create_OutInsufficientStock_Throws400_AndChangesNothing()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P003", stock: 0);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.CreateAsync(ValidRequest(productId, "OUT", 1), "user-1"));

        // 库存不足返回 400 INSUFFICIENT_STOCK（业务冲突不落 500）
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ErrorCodes.InsufficientStock, ex.Code);

        // 失败不留下任何变化：库存仍为 0，无新流水（E07）
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.Equal(0, product.CurrentStock);
        Assert.Equal(0, await db.StockTransactions.CountAsync());
    }

    [Fact]
    public async Task Create_OutMoreThanStock_Throws400()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P004", stock: 3);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.CreateAsync(ValidRequest(productId, "OUT", 4), "user-1"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ErrorCodes.InsufficientStock, ex.Code);
    }

    // ---------------- 幂等（E13） ----------------

    [Fact]
    public async Task Create_SameRequestIdSamePayload_ReturnsFirstResult_WithoutReExecution()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P002", stock: 10);
        var requestId = Guid.NewGuid().ToString();

        var first = await service.CreateAsync(ValidRequest(productId, "OUT", 4, requestId), "user-1");
        var replay = await service.CreateAsync(ValidRequest(productId, "OUT", 4, requestId), "user-2");

        Assert.True(first.Created);
        Assert.False(replay.Created);

        // 重放返回首次成功结果（同一笔流水）
        Assert.Equal(first.Dto.Id, replay.Dto.Id);
        Assert.Equal(first.Dto.TransactionReference, replay.Dto.TransactionReference);
        Assert.Equal(6, replay.Dto.NewStock);

        // 仅一次扣减：只有 1 条流水，库存只扣一次（E13）
        Assert.Equal(1, await db.StockTransactions.CountAsync());
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.Equal(6, product.CurrentStock);
    }

    [Fact]
    public async Task Create_SameRequestIdDifferentPayload_Throws409IdempotencyConflict()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P005", stock: 10);
        var requestId = Guid.NewGuid().ToString();

        await service.CreateAsync(ValidRequest(productId, "IN", 5, requestId), "user-1");

        // 相同 RequestId、不同内容 → 409 IDEMPOTENCY_CONFLICT
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.CreateAsync(ValidRequest(productId, "OUT", 4, requestId), "user-1"));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(ErrorCodes.IdempotencyConflict, ex.Code);

        // 首次结果不受影响
        Assert.Equal(1, await db.StockTransactions.CountAsync());
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.Equal(15, product.CurrentStock);
    }

    [Fact]
    public async Task Create_ConcurrentSameRequestId_ExecutesOnlyOnce()
    {
        var newDb = CreateSharedDbFactory();
        long productId;
        using (var seedDb = newDb())
        {
            var categoryId = TestHost.CreateCategory(seedDb, "Electronics");
            productId = CreateProduct(seedDb, categoryId, "P006", stock: 100);
        }

        // 5 个并发任务使用相同 RequestId + 相同负载：只应执行一次扣减
        var requestId = Guid.NewGuid().ToString();
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
        {
            using var db = newDb();
            var service = new StockTransactionService(db);
            return await service.CreateAsync(ValidRequest(productId, "OUT", 10, requestId), "user-1");
        })));

        // 恰好 1 次首次创建，其余均为幂等重放，且都拿到同一笔结果
        Assert.Equal(1, results.Count(r => r.Created));
        Assert.All(results, r => Assert.Equal(results[0].Dto.Id, r.Dto.Id));
        Assert.Equal(results[0].Dto.NewStock, results.Select(r => r.Dto.NewStock).Distinct().Single());

        using var verifyDb = newDb();
        var product = await verifyDb.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.Equal(90, product.CurrentStock); // 只扣减一次
        Assert.Equal(1, await verifyDb.StockTransactions.CountAsync());
    }

    // ---------------- 并发防超卖（E09） ----------------

    [Fact]
    public async Task Create_ConcurrentOut_ExactlyFiveSucceed_NoOversell()
    {
        var newDb = CreateSharedDbFactory();
        long productId;
        using (var seedDb = newDb())
        {
            var categoryId = TestHost.CreateCategory(seedDb, "Electronics");
            productId = CreateProduct(seedDb, categoryId, "P008", stock: 5);
        }

        // 并发发起 10 个不同 RequestId 的 OUT 1：恰有 5 次成功、5 次库存不足（E09）
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            using var db = newDb();
            var service = new StockTransactionService(db);
            try
            {
                var dto = await service.CreateAsync(ValidRequest(productId, "OUT", 1), "user-1");
                return (Success: true, Error: (ApiException?)null, Dto: (StockTransactionDto?)dto.Dto);
            }
            catch (ApiException ex)
            {
                return (Success: false, Error: (ApiException?)ex, Dto: (StockTransactionDto?)null);
            }
        })));

        Assert.Equal(5, results.Count(r => r.Success));
        Assert.Equal(5, results.Count(r => r.Error is { Code: ErrorCodes.InsufficientStock }));

        // 最终库存 0，成功流水恰好 5 条，不允许超卖
        using var verifyDb = newDb();
        var product = await verifyDb.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.Equal(0, product.CurrentStock);
        Assert.Equal(5, await verifyDb.StockTransactions.CountAsync());

        // 每笔成功流水的 PreviousStock/NewStock 连续一致，无负库存
        var txs = await verifyDb.StockTransactions.AsNoTracking()
            .OrderBy(t => t.NewStock).ToListAsync();
        Assert.Equal(0, txs.Min(t => t.NewStock));
        Assert.All(txs, t => Assert.Equal(t.PreviousStock - 1, t.NewStock));
    }

    // ---------------- 参数校验（E11） ----------------

    [Theory]
    [InlineData("IN", 0)]
    [InlineData("IN", -1)]
    [InlineData("OUT", 0)]
    [InlineData("OUT", -3)]
    public async Task Create_NonPositiveQuantityForInOut_Throws400Validation(string type, int quantity)
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P001", stock: 5);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.CreateAsync(ValidRequest(productId, type, quantity), "user-1"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, ex.Code);
        Assert.Equal(0, await db.StockTransactions.CountAsync()); // 数据库未改变
    }

    [Fact]
    public async Task Create_NegativeQuantityForAdjust_Throws400Validation()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P001", stock: 5);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.CreateAsync(ValidRequest(productId, "ADJUST", -1), "admin-1"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, ex.Code);
    }

    [Fact]
    public async Task Create_InvalidType_Throws400Validation()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P001", stock: 5);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.CreateAsync(ValidRequest(productId, "TRANSFER", 1), "user-1"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("01234567-89ab-4cde-8fab")] // 缺一段
    public async Task Create_InvalidRequestId_Throws400Validation(string requestId)
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P001", stock: 5);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.CreateAsync(ValidRequest(productId, "IN", 1, requestId), "user-1"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, ex.Code);
        Assert.Equal(0, await db.StockTransactions.CountAsync());
    }

    [Fact]
    public async Task Create_InOverflow_Throws400Validation()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P001", stock: 5);

        // IN 导致 int 溢出必须被拦截，返回 400 而非 500（需求：需防止 int 溢出）
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.CreateAsync(ValidRequest(productId, "IN", int.MaxValue), "user-1"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, ex.Code);
        Assert.Equal(0, await db.StockTransactions.CountAsync());
    }

    [Fact]
    public async Task Create_NonexistentProduct_Throws404NotFound()
    {
        var (service, db) = CreateService();
        TestHost.CreateCategory(db, "Electronics");

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.CreateAsync(ValidRequest(999, "IN", 1), "user-1"));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, ex.Code);
        Assert.Equal(0, await db.StockTransactions.CountAsync());
    }

    // ---------------- 流水分页查询 ----------------

    [Fact]
    public async Task Query_FiltersAndOrdersByCreatedAtDescIdDesc()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P001", stock: 0);
        var otherProductId = CreateProduct(db, categoryId, "P002", stock: 0);

        var baseTime = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        db.StockTransactions.AddRange(
            new StockTransaction
            {
                TransactionReference = "TX-1", RequestId = Guid.NewGuid().ToString(), ProductId = productId,
                Type = "IN", Quantity = 5, PreviousStock = 0, NewStock = 5,
                CreatedAt = baseTime, ActorUserId = "u1",
            },
            new StockTransaction
            {
                TransactionReference = "TX-2", RequestId = Guid.NewGuid().ToString(), ProductId = productId,
                Type = "OUT", Quantity = 2, PreviousStock = 5, NewStock = 3,
                CreatedAt = baseTime.AddHours(1), ActorUserId = "u1",
            },
            new StockTransaction
            {
                TransactionReference = "TX-3", RequestId = Guid.NewGuid().ToString(), ProductId = otherProductId,
                Type = "IN", Quantity = 9, PreviousStock = 0, NewStock = 9,
                CreatedAt = baseTime.AddHours(2), ActorUserId = "u1",
            });
        await db.SaveChangesAsync();

        // 按商品过滤
        var byProduct = await service.QueryAsync(productId, null, null, null, 1, 20);
        Assert.Equal(2, byProduct.Total);
        Assert.All(byProduct.Items, t => Assert.Equal(productId, t.ProductId));

        // 按类型过滤
        var byType = await service.QueryAsync(null, "out", null, null, 1, 20);
        Assert.Equal(1, byType.Total);
        Assert.Equal("OUT", byType.Items[0].Type);

        // fromUtc 包含、toUtc 不包含（契约）
        var range = await service.QueryAsync(null, null, baseTime, baseTime.AddHours(1), 1, 20);
        Assert.Equal(1, range.Total);
        Assert.Equal("TX-1", range.Items[0].TransactionReference);

        // CreatedAt DESC 排序（最新优先）
        var all = await service.QueryAsync(null, null, null, null, 1, 20);
        Assert.Equal(3, all.Total);
        Assert.Equal("TX-3", all.Items[0].TransactionReference);
        Assert.Equal("TX-2", all.Items[1].TransactionReference);
    }

    [Fact]
    public async Task Query_InvalidTypeFilter_Throws400()
    {
        var (service, db) = CreateService();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.QueryAsync(null, "WRONG", null, null, 1, 20));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Query_PaginationCapsPageSizeAt100()
    {
        var (service, db) = CreateService();
        var paged = await service.QueryAsync(null, null, null, null, 1, 500);
        Assert.Equal(100, paged.PageSize); // pageSize 最大 100（契约）
        Assert.Equal(1, paged.Page);
    }
}
