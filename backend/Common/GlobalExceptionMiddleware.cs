using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace P03.Inventory.Api.Common;

/// <summary>全局异常处理：所有非 2xx 统一返回 { code, message, details, traceId }，业务冲突不落 500。</summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ApiException ex)
        {
            await WriteErrorAsync(context, ex.StatusCode, ex.Code, ex.Message, ex.Details);
        }
        catch (DbUpdateConcurrencyException)
        {
            await WriteErrorAsync(context, StatusCodes.Status409Conflict, ErrorCodes.StockConflict,
                "The record was modified by another operation. Please retry.");
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex, out var constraint))
        {
            _logger.LogWarning(ex, "Duplicate key violation on {Constraint}", constraint);
            await WriteErrorAsync(context, StatusCodes.Status409Conflict, ErrorCodes.DuplicateValue,
                "The value already exists.");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 客户端主动取消，无需响应体
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError, ErrorCodes.InternalError,
                "An unexpected error occurred. Please try again later.");
        }
    }

    private static async Task WriteErrorAsync(
        HttpContext context, int statusCode, string code, string message, object? details = null)
    {
        if (context.Response.HasStarted)
        {
            throw new InvalidOperationException("Cannot write error response: response has already started.");
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = new ErrorResponse(code, message, details, TraceIdMiddleware.Get(context));
        await context.Response.WriteAsJsonAsync(body);
    }

    private static bool IsDuplicateKey(DbUpdateException ex, out string constraintName)
    {
        constraintName = string.Empty;
        if (ex.InnerException is not SqlException sql) return false;
        // 2601: unique index; 2627: unique constraint
        if (sql.Number is not (2601 or 2627)) return false;
        var message = sql.Message ?? string.Empty;
        var start = message.IndexOf('\'');
        var end = start >= 0 ? message.IndexOf('\'', start + 1) : -1;
        if (start >= 0 && end > start) constraintName = message[(start + 1)..end];
        return true;
    }
}
