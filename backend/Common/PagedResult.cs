namespace P03.Inventory.Api.Common;

/// <summary>统一分页响应结构：{ items, total, page, pageSize }。page 从 1 起。</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public static PagedResult<T> Create(IReadOnlyList<T> items, int total, int page, int pageSize)
        => new(items, total, page, pageSize);
}
