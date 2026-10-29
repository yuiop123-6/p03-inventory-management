using Microsoft.EntityFrameworkCore;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Data;
using P03.Inventory.Api.Dtos;

namespace P03.Inventory.Api.Services;

public interface IDashboardService
{
    /// <summary>仪表盘统计（纯只读查询，不修改任何数据）。</summary>
    Task<DashboardDto> GetAsync(CancellationToken ct = default);
}

public class DashboardService : IDashboardService
{
    /// <summary>最近流水条数（契约：最近 10 条）。</summary>
    public const int RecentTransactionCount = 10;

    /// <summary>净变更统计窗口天数（契约：过去 7 个 UTC 自然日，零填充）。</summary>
    public const int TrailingDays = 7;

    private readonly AppDbContext _db;

    public DashboardService(AppDbContext db) => _db = db;

    public async Task<DashboardDto> GetAsync(CancellationToken ct = default)
    {
        // ---------- 4 项核心指标（AsNoTracking 只读） ----------
        var products = _db.Products.AsNoTracking();
        var totalProducts = await products.CountAsync(ct);
        var totalStockQuantity = await products.SumAsync(p => (int?)p.CurrentStock, ct) ?? 0;

        // 口径（E15，互斥不重复计数）：缺货 stock == 0；
        // 低库存 0 < stock <= 阈值（等于阈值属低库存）；正常 stock > 阈值
        var lowStockProducts = await products
            .CountAsync(p => p.CurrentStock > 0 && p.CurrentStock <= p.MinimumStockLevel, ct);
        var outOfStockProducts = await products.CountAsync(p => p.CurrentStock == 0, ct);

        // ---------- 最近 10 条流水（CreatedAt DESC, Id DESC） ----------
        var recentTransactions = await _db.StockTransactions.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Take(RecentTransactionCount)
            .Select(t => StockTransactionService.Map(t))
            .ToListAsync(ct);

        // ---------- 过去 7 个 UTC 自然日（含今天）每日净变更，无交易日填 0（E16） ----------
        var todayUtc = DateTime.UtcNow.Date;
        var windowStart = todayUtc.AddDays(-(TrailingDays - 1));

        var dailySums = await _db.StockTransactions.AsNoTracking()
            .Where(t => t.CreatedAt >= windowStart)
            .GroupBy(t => t.CreatedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                // IN 计 +Quantity；OUT 计 −Quantity；ADJUST 记录目标调整差值
                NetChange = g.Sum(x => x.Type == StockTransactionService.TypeIn ? x.Quantity
                    : x.Type == StockTransactionService.TypeOut ? -x.Quantity
                    : x.NewStock - x.PreviousStock),
            })
            .ToListAsync(ct);

        var sumsByDate = dailySums.ToDictionary(x => x.Date, x => x.NetChange);
        var dailyNetChanges = Enumerable.Range(0, TrailingDays)
            .Select(offset => windowStart.AddDays(offset))
            .Select(date => new DailyNetChangeDto(
                DateTime.SpecifyKind(date, DateTimeKind.Utc),
                sumsByDate.GetValueOrDefault(date))) // 无交易的日期填充 0
            .ToList();

        return new DashboardDto(
            totalProducts,
            totalStockQuantity,
            lowStockProducts,
            outOfStockProducts,
            recentTransactions,
            dailyNetChanges);
    }
}
