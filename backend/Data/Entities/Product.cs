namespace P03.Inventory.Api.Data.Entities;

/// <summary>商品。ProductCode 唯一且不可在编辑接口修改；删除受流水/库存保护。</summary>
public class Product
{
    public long Id { get; set; }

    /// <summary>商品编号，nvarchar(40)，必填唯一，保存前 Trim + 转大写，不可编辑。</summary>
    public string ProductCode { get; set; } = null!;

    /// <summary>商品名，nvarchar(200)，必填。</summary>
    public string Name { get; set; } = null!;

    /// <summary>描述，nvarchar(1000)，可空。</summary>
    public string? Description { get; set; }

    public long CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    /// <summary>单价 decimal(18,2)，>= 0。</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>当前库存 int，>= 0；编辑接口不可修改，仅库存事务变更。</summary>
    public int CurrentStock { get; set; }

    /// <summary>最低库存阈值 int，>= 0。</summary>
    public int MinimumStockLevel { get; set; }

    /// <summary>创建时间（UTC），不可编辑。</summary>
    public DateTime CreatedAt { get; set; }

    public ICollection<StockTransaction> Transactions { get; set; } = new List<StockTransaction>();
}
