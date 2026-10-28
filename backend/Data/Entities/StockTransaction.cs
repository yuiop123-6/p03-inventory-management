namespace P03.Inventory.Api.Data.Entities;

/// <summary>库存流水（成员B实现业务）。库存变更与流水写入由业务层保证同事务提交。</summary>
public class StockTransaction
{
    public long Id { get; set; }

    /// <summary>业务引用编号，nvarchar(64)，服务端生成，唯一（如 TX-000101）。</summary>
    public string TransactionReference { get; set; } = null!;

    /// <summary>调用方幂等键（UUID），nvarchar(64)，唯一。重放返回首次结果。</summary>
    public string RequestId { get; set; } = null!;

    /// <summary>请求负载哈希（幂等冲突检测用），nvarchar(64)，可空。</summary>
    public string? RequestHash { get; set; }

    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>IN / OUT / ADJUST，数据库 CHECK 约束限定。</summary>
    public string Type { get; set; } = null!;

    /// <summary>IN/OUT 为正整数变动量；ADJUST 为新的目标库存（允许 0）。</summary>
    public int Quantity { get; set; }

    /// <summary>操作前库存，>= 0。</summary>
    public int PreviousStock { get; set; }

    /// <summary>操作后库存，>= 0。</summary>
    public int NewStock { get; set; }

    /// <summary>备注，nvarchar(1000)，可空。</summary>
    public string? Notes { get; set; }

    /// <summary>创建时间（UTC），服务端产生。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>操作人（Identity 用户 Id）。</summary>
    public string ActorUserId { get; set; } = null!;
}
