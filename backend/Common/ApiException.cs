namespace P03.Inventory.Api.Common;

/// <summary>业务异常：携带 HTTP 状态码与统一错误码，由 GlobalExceptionMiddleware 转为统一错误响应。</summary>
public class ApiException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }
    public object? Details { get; }

    public ApiException(int statusCode, string code, string message, object? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Details = details;
    }

    public static ApiException Validation(string message, object? details = null)
        => new(StatusCodes.Status400BadRequest, ErrorCodes.ValidationError, message, details);

    public static ApiException NotFound(string message, object? details = null)
        => new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, message, details);

    public static ApiException Duplicate(string message, object? details = null)
        => new(StatusCodes.Status409Conflict, ErrorCodes.DuplicateValue, message, details);

    public static ApiException Referential(string message, object? details = null)
        => new(StatusCodes.Status409Conflict, ErrorCodes.ReferentialConflict, message, details);
}
