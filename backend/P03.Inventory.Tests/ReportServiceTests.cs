using Microsoft.EntityFrameworkCore;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Data;
using P03.Inventory.Api.Data.Entities;
using P03.Inventory.Api.Services;
using Xunit;

namespace P03.Inventory.Tests;

/// <summary>报表服务单元测试（成员B）：商品库存报表（含库存金额）与历史流水报表的过滤、分页、排序。</summary>
public class ReportServiceTests
{
    private static (ReportService Service, AppDbContext Db) CreateService()
    {
        var db = TestHost.CreateDb();
        return (new ReportService(db, new StockTransactionService(db)), db);
    }

    [Fact]
    public async Task ProductReport_StockAmount_EqualsUnitPriceTimesCurrentStock()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        TestHost.CreateProduct(db, categoryId, "P001", "Wireless Mouse", price: 29.90m, stock: 35);

        var report = await service.ProductReportAsync(null, null, null, 1, 20);

        var item = Assert.Single(report.Items);
        Assert.Equal(1046.50m, item.StockAmount); // 29.90 × 35
        Assert.Equal("Electronics", item.CategoryName);
    }

    [Fact]
    public async Task ProductReport_FiltersByCategorySearchAndStockStatus()
    {
        var (service, db) = CreateService();
        var electronics = TestHost.CreateCategory(db, "Electronics");
        var furniture = TestHost.CreateCategory(db, "Furniture");

        TestHost.CreateProduct(db, electronics, "P001", "Wireless Mouse", stock: 35, minStock: 10); // 正常
        TestHost.CreateProduct(db, electronics, "P002", "Keyboard Pro", stock: 10, minStock: 10);   // 低库存（等于阈值）
        TestHost.CreateProduct(db, electronics, "P003", "USB Hub", stock: 0, minStock: 5);          // 缺货
        TestHost.CreateProduct(db, furniture, "P004", "Office Chair", stock: 8, minStock: 2);       // 正常（其他分类）

        // 分类过滤
        var byCategory = await service.ProductReportAsync(null, electronics, null, 1, 20);
        Assert.Equal(3, byCategory.Total);

        // 关键字搜索（名称包含，忽略前后空格）
        // 注：InMemory 提供程序的 Contains 为序数比较（区分大小写），
        // SQL Server 采用 CI 排序规则不区分大小写；此处用与目标大小写一致的关键字验证 Trim + 包含逻辑
        var bySearch = await service.ProductReportAsync(" Keyboard ", null, null, 1, 20);
        Assert.Equal(1, bySearch.Total);
        Assert.Equal("Keyboard Pro", bySearch.Items[0].Name);

        // 库存状态过滤：low 含等于阈值；out 为 0；normal 大于阈值（E15 口径）
        var low = await service.ProductReportAsync(null, null, "low", 1, 20);
        Assert.Equal(1, low.Total);
        Assert.Equal("P002", low.Items[0].ProductCode);

        var outOfStock = await service.ProductReportAsync(null, null, "out", 1, 20);
        Assert.Equal(1, outOfStock.Total);
        Assert.Equal("P003", outOfStock.Items[0].ProductCode);

        var normal = await service.ProductReportAsync(null, null, "normal", 1, 20);
        Assert.Equal(2, normal.Total);

        // 组合过滤：分类 + 状态
        var combined = await service.ProductReportAsync(null, electronics, "out", 1, 20);
        Assert.Equal(1, combined.Total);
        Assert.Equal("USB Hub", combined.Items[0].Name);
    }

    [Fact]
    public async Task ProductReport_PagingIsStableAndOrderedByIdAsc()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        for (var i = 1; i <= 25; i++)
        {
            TestHost.CreateProduct(db, categoryId, $"P{i:D3}", $"Product {i}");
        }

        var page1 = await service.ProductReportAsync(null, null, null, 1, 10);
        var page2 = await service.ProductReportAsync(null, null, null, 2, 10);
        var page3 = await service.ProductReportAsync(null, null, null, 3, 10);

        Assert.Equal(25, page1.Total);
        Assert.Equal(10, page1.PageSize);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(10, page2.Items.Count);
        Assert.Equal(5, page3.Items.Count);

        // 按 Id 升序稳定分页（契约，重复查询排序稳定）
        var all = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(p => p.Id).ToList();
        Assert.Equal(all.Order().ToList(), all);

        // 超出范围返回空列表而非报错
        var empty = await service.ProductReportAsync(null, null, null, 9, 10);
        Assert.Empty(empty.Items);
        Assert.Equal(25, empty.Total);
    }

    [Fact]
    public async Task ProductReport_InvalidStockStatus_Throws400()
    {
        var (service, db) = CreateService();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.ProductReportAsync(null, null, "broken", 1, 20));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, ex.Code);
    }

    [Fact]
    public async Task TransactionReport_FiltersSortsAndPages()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = TestHost.CreateProduct(db, categoryId, "P001", "P001 Name", stock: 0);
        var otherProductId = TestHost.CreateProduct(db, categoryId, "P002", "P002 Name", stock: 0);

        var baseTime = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);
        db.StockTransactions.AddRange(
            new StockTransaction
            {
                TransactionReference = "TX-R1", RequestId = Guid.NewGuid().ToString(), ProductId = productId,
                Type = "IN", Quantity = 5, PreviousStock = 0, NewStock = 5,
                CreatedAt = baseTime, ActorUserId = "u1",
            },
            new StockTransaction
            {
                TransactionReference = "TX-R2", RequestId = Guid.NewGuid().ToString(), ProductId = productId,
                Type = "OUT", Quantity = 2, PreviousStock = 5, NewStock = 3,
                CreatedAt = baseTime.AddDays(1), ActorUserId = "u1",
            },
            new StockTransaction
            {
                TransactionReference = "TX-R3", RequestId = Guid.NewGuid().ToString(), ProductId = otherProductId,
                Type = "ADJUST", Quantity = 9, PreviousStock = 3, NewStock = 9,
                CreatedAt = baseTime.AddDays(2), ActorUserId = "u1",
            });
        await db.SaveChangesAsync();

        // 按商品过滤
        var byProduct = await service.TransactionReportAsync(productId, null, null, null, 1, 20);
        Assert.Equal(2, byProduct.Total);

        // fromUtc 包含 / toUtc 不包含（UTC 边界，E16）
        var range = await service.TransactionReportAsync(null, null, baseTime.AddDays(1), baseTime.AddDays(2), 1, 20);
        var item = Assert.Single(range.Items);
        Assert.Equal("TX-R2", item.TransactionReference);

        // 默认最新优先（CreatedAt DESC）
        var latest = await service.TransactionReportAsync(null, null, null, null, 1, 20);
        Assert.Equal("TX-R3", latest.Items[0].TransactionReference);

        // 分页
        var page2 = await service.TransactionReportAsync(null, null, null, null, 2, 2);
        Assert.Equal(3, page2.Total);
        Assert.Equal("TX-R1", page2.Items[0].TransactionReference);
    }
}
