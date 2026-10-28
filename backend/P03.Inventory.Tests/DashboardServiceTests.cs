using Microsoft.EntityFrameworkCore;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Data;
using P03.Inventory.Api.Data.Entities;
using P03.Inventory.Api.Services;
using Xunit;

namespace P03.Inventory.Tests;

/// <summary>仪表盘服务单元测试（成员B）：指标口径（E15）、最近 10 条流水、7 日净变更零填充（E16）。</summary>
public class DashboardServiceTests
{
    private static (DashboardService Service, AppDbContext Db) CreateService()
    {
        var db = TestHost.CreateDb();
        return (new DashboardService(db), db);
    }

    /// <summary>本地便捷建商品：名称默认派生自编码（TestHost.CreateProduct 需显式名称）。</summary>
    private static long CreateProduct(AppDbContext db, long categoryId, string code, int stock = 0, int minStock = 0)
        => TestHost.CreateProduct(db, categoryId, code, $"{code} Name", stock: stock, minStock: minStock);

    private static StockTransaction MakeTransaction(long productId, string type, int quantity,
        int previousStock, int newStock, DateTime createdAt, int seq)
        => new()
        {
            TransactionReference = $"TX-T{seq:D4}",
            RequestId = Guid.NewGuid().ToString(),
            ProductId = productId,
            Type = type,
            Quantity = quantity,
            PreviousStock = previousStock,
            NewStock = newStock,
            CreatedAt = createdAt,
            ActorUserId = "u1",
        };

    [Fact]
    public async Task Get_EmptyDatabase_ReturnsZerosAndEmptyRecent()
    {
        var (service, db) = CreateService();
        TestHost.CreateCategory(db, "Electronics"); // 空库也可能有分类，无商品

        var dto = await service.GetAsync();

        // 初始无数据：4 项指标全 0；最近流水为空列表；7 天全为零（E01）
        Assert.Equal(0, dto.TotalProducts);
        Assert.Equal(0, dto.TotalStockQuantity);
        Assert.Equal(0, dto.LowStockProducts);
        Assert.Equal(0, dto.OutOfStockProducts);
        Assert.Empty(dto.RecentTransactions);
        Assert.Equal(7, dto.DailyNetChanges.Count);
        Assert.All(dto.DailyNetChanges, d => Assert.Equal(0, d.NetChange));
    }

    [Fact]
    public async Task Get_DailyDates_AreLastSevenUtcDaysEndingToday()
    {
        var (service, _) = CreateService();

        var dto = await service.GetAsync();

        var todayUtc = DateTime.UtcNow.Date;
        Assert.Equal(todayUtc.AddDays(-6), dto.DailyNetChanges[0].Date);
        Assert.Equal(todayUtc, dto.DailyNetChanges[^1].Date);
        Assert.Equal(DateTimeKind.Utc, dto.DailyNetChanges[0].Date.Kind);
        // 日期连续
        for (var i = 1; i < dto.DailyNetChanges.Count; i++)
        {
            Assert.Equal(dto.DailyNetChanges[i - 1].Date.AddDays(1), dto.DailyNetChanges[i].Date);
        }
    }

    [Fact]
    public async Task Get_Metrics_UseExclusiveDefinitions_NoDoubleCounting()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");

        // 低库存含等于阈值（E15）：stock == min 属低库存；缺货 stock == 0；互斥不重复计数
        CreateProduct(db, categoryId, "P001", stock: 5, minStock: 5);  // 低库存（等于阈值）
        CreateProduct(db, categoryId, "P002", stock: 3, minStock: 10); // 低库存
        CreateProduct(db, categoryId, "P003", stock: 0, minStock: 5);  // 缺货
        CreateProduct(db, categoryId, "P004", stock: 100, minStock: 1); // 正常
        CreateProduct(db, categoryId, "P005", stock: 0, minStock: 0);  // 缺货

        var dto = await service.GetAsync();

        Assert.Equal(5, dto.TotalProducts);
        Assert.Equal(108, dto.TotalStockQuantity); // 5 + 3 + 0 + 100 + 0
        Assert.Equal(2, dto.LowStockProducts);
        Assert.Equal(2, dto.OutOfStockProducts);
    }

    [Fact]
    public async Task Get_RecentTransactions_ReturnsAtMost10NewestFirst()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P001", stock: 0);

        var baseTime = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 12; i++)
        {
            db.StockTransactions.Add(MakeTransaction(productId, "IN", 1, 0, 1, baseTime.AddHours(i), i));
        }
        await db.SaveChangesAsync();

        var dto = await service.GetAsync();

        // 最多 10 条，CreatedAt DESC（最新优先）
        Assert.Equal(10, dto.RecentTransactions.Count);
        Assert.Equal("TX-T0011", dto.RecentTransactions[0].TransactionReference); // 第 12 条（最新）
        Assert.Equal("TX-T0002", dto.RecentTransactions[^1].TransactionReference); // 第 3 条（第 10 新）
    }

    [Fact]
    public async Task Get_DailyNetChanges_SignedDeltas_AndZeroFill()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P001", stock: 100);

        var todayUtc = DateTime.UtcNow.Date;
        db.StockTransactions.AddRange(
            // 今天：IN +3
            MakeTransaction(productId, "IN", 3, 97, 100, todayUtc.AddHours(1), 1),
            // 昨天：OUT −2
            MakeTransaction(productId, "OUT", 2, 99, 97, todayUtc.AddDays(-1).AddHours(1), 2),
            // 3 天前：ADJUST 10 → 4，净变更 −6
            MakeTransaction(productId, "ADJUST", 4, 10, 4, todayUtc.AddDays(-3).AddHours(1), 3));
        await db.SaveChangesAsync();

        var dto = await service.GetAsync();

        var byDate = dto.DailyNetChanges.ToDictionary(d => d.Date, d => d.NetChange);
        Assert.Equal(3, byDate[todayUtc]);                                 // IN 计正
        Assert.Equal(-2, byDate[todayUtc.AddDays(-1)]);                    // OUT 计负
        Assert.Equal(-6, byDate[todayUtc.AddDays(-3)]);                    // ADJUST 取差值
        Assert.Equal(0, byDate[todayUtc.AddDays(-2)]);                     // 无交易填 0
        Assert.Equal(0, byDate[todayUtc.AddDays(-6)]);                     // 窗口首日填 0
        Assert.Equal(7, dto.DailyNetChanges.Count);                        // 恒为 7 天
    }

    [Fact]
    public async Task Get_OldTransactionsOutsideWindow_AreNotCountedInDailyChanges()
    {
        var (service, db) = CreateService();
        var categoryId = TestHost.CreateCategory(db, "Electronics");
        var productId = CreateProduct(db, categoryId, "P001", stock: 0);

        var todayUtc = DateTime.UtcNow.Date;
        // 8 天前的交易不在 7 日窗口内，只出现在“最近流水”里（若足够新），不影响净变更
        db.StockTransactions.Add(MakeTransaction(productId, "IN", 50, 0, 50,
            todayUtc.AddDays(-8), 1));
        await db.SaveChangesAsync();

        var dto = await service.GetAsync();

        Assert.All(dto.DailyNetChanges, d => Assert.Equal(0, d.NetChange)); // 窗口外不计数
        Assert.Equal(1, await db.StockTransactions.CountAsync());
    }
}
