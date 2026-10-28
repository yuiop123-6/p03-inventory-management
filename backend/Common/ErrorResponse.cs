using System.Text.Json.Serialization;

namespace P03.Inventory.Api.Common;

/// <summary>统一错误响应结构：{ code, message, details, traceId }。</summary>
public record ErrorResponse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("details")] object? Details,
    [property: JsonPropertyName("traceId")] string TraceId);
