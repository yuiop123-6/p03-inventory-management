using System.Diagnostics;

namespace P03.Inventory.Api.Common;

/// <summary>为每个请求生成/透出 TraceId：响应头 X-Trace-Id + HttpContext.Items，供错误响应体引用。</summary>
public class TraceIdMiddleware
{
    private const string HeaderName = "X-Trace-Id";
    public const string ItemKey = "P03.TraceId";

    private readonly RequestDelegate _next;

    public TraceIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        context.Items[ItemKey] = traceId;
        context.Response.Headers[HeaderName] = traceId;
        await _next(context);
    }

    public static string Get(HttpContext context)
        => context.Items.TryGetValue(ItemKey, out var value) && value is string s
            ? s
            : context.TraceIdentifier;
}
