using System.ComponentModel.DataAnnotations;

namespace P03.Inventory.Api.Dtos;

public record ProductDto(
    long Id,
    string ProductCode,
    string Name,
    string? Description,
    long CategoryId,
    string CategoryName,
    decimal UnitPrice,
    int CurrentStock,
    int MinimumStockLevel,
    DateTime CreatedAt,
    string StockStatus);

/// <summary>新增商品：初始库存作为种子/期初值，不产生库存交易。</summary>
public class CreateProductRequest
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(40)]
    public string ProductCode { get; set; } = null!;

    [Required(AllowEmptyStrings = false)]
    [MaxLength(200)]
    public string Name { get; set; } = null!;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Range(1, long.MaxValue)]
    public long CategoryId { get; set; }

    [Range(0, 999999999999.99)]
    public decimal UnitPrice { get; set; }

    [Range(0, int.MaxValue)]
    public int CurrentStock { get; set; }

    [Range(0, int.MaxValue)]
    public int MinimumStockLevel { get; set; }
}

/// <summary>编辑商品：仅允许 name/description/categoryId/unitPrice/minimumStockLevel；
/// productCode/currentStock/createdAt 不可修改（请求中出现时由控制器返回 400）。</summary>
public class UpdateProductRequest
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(200)]
    public string Name { get; set; } = null!;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Range(1, long.MaxValue)]
    public long CategoryId { get; set; }

    [Range(0, 999999999999.99)]
    public decimal UnitPrice { get; set; }

    [Range(0, int.MaxValue)]
    public int MinimumStockLevel { get; set; }
}
