namespace P03.Inventory.Api.Data.Entities;

/// <summary>商品分类。Name 忽略大小写与首尾空格后唯一（数据库层经持久化计算列 + 唯一索引强制）。</summary>
public class Category
{
    public long Id { get; set; }

    /// <summary>分类名，nvarchar(100)，必填，保存前 Trim。</summary>
    public string Name { get; set; } = null!;

    /// <summary>描述，nvarchar(500)，可空。</summary>
    public string? Description { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
