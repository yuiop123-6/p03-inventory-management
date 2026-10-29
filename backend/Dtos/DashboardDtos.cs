namespace P03.Inventory.Api.Dtos;

/// <summary>仪表盘响应：4 项核心指标 + 最近 10 条流水 + 过去 7 个 UTC 自然日净变更（无交易填 0）。
/// 指标字段名与 seed-data/expected_initial_dashboard.json 对齐。</summary>
public record DashboardDto(
    int TotalProducts,
    int TotalStockQuantity,
    int LowStockProducts,
    int OutOfStockProducts,
    IReadOnlyList<StockTransactionDto> RecentTransactions,
    IReadOnlyList<DailyNetChangeDto> DailyNetChanges);

/// <summary>单个 UTC 自然日的净库存变更：
/// IN 计 +Quantity，OUT 计 −Quantity，ADJUST 计 NewStock − PreviousStock。</summary>
public record DailyNetChangeDto(DateTime Date, int NetChange);
