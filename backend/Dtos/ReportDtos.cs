namespace P03.Inventory.Api.Dtos;

/// <summary>商品库存报表行（成员B）：与商品查询同口径 + 库存金额 = unitPrice × currentStock。</summary>
public record ReportProductDto(
    long Id,
    string ProductCode,
    string Name,
    long CategoryId,
    string CategoryName,
    decimal UnitPrice,
    int CurrentStock,
    int MinimumStockLevel,
    string StockStatus,
    decimal StockAmount);
