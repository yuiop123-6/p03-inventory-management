using System.ComponentModel.DataAnnotations;

namespace P03.Inventory.Api.Dtos;

/// <summary>创建库存事务请求（成员B，API 契约：requestId/productId/type/quantity/notes）。</summary>
public class CreateStockTransactionRequest
{
    /// <summary>调用方生成的幂等键 UUID，必填。相同 RequestId 重放返回首次结果。</summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(64)]
    public string RequestId { get; set; } = null!;

    [Range(1, long.MaxValue)]
    public long ProductId { get; set; }

    /// <summary>IN（入库）/ OUT（出库）/ ADJUST（库存调整，仅 Admin）。</summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(10)]
    public string Type { get; set; } = null!;

    /// <summary>IN/OUT 为正整数变动量；ADJUST 为新的目标库存（允许 0）。</summary>
    [Range(0, int.MaxValue)]
    public int Quantity { get; set; }

    /// <summary>备注，可空，最长 1000 字符。</summary>
    [MaxLength(1000)]
    public string? Notes { get; set; }
}

/// <summary>库存事务响应，字段与 docs/api-contract.md 一一对应。</summary>
public record StockTransactionDto(
    long Id,
    string TransactionReference,
    string RequestId,
    long ProductId,
    string Type,
    int Quantity,
    int PreviousStock,
    int NewStock,
    string? Notes,
    DateTime CreatedAt);
