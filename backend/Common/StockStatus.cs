namespace P03.Inventory.Api.Common;

/// <summary>库存状态口径（与验收清单 stockDefinitions 一致）。</summary>
public static class StockStatus
{
    public const string Normal = "normal";
    public const string Low = "low";
    public const string Out = "out";
    public const string All = "all";

    /// <summary>缺货：stock == 0；低库存：0 &lt; stock &lt;= minimumStockLevel；正常：stock &gt; minimumStockLevel。</summary>
    public static string Of(int currentStock, int minimumStockLevel)
    {
        if (currentStock == 0) return Out;
        if (currentStock <= minimumStockLevel) return Low;
        return Normal;
    }
}
