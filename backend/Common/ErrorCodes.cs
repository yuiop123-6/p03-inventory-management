namespace P03.Inventory.Api.Common;

/// <summary>统一业务错误码（与 API 契约 docs/api-contract.md 保持一致）。</summary>
public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string DuplicateValue = "DUPLICATE_VALUE";
    public const string ReferentialConflict = "REFERENTIAL_CONFLICT";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string StockConflict = "STOCK_CONFLICT";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string RateLimited = "RATE_LIMITED";
    public const string InternalError = "INTERNAL_ERROR";
}
