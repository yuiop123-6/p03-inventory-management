using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Dtos;
using P03.Inventory.Api.Services;

namespace P03.Inventory.Api.Controllers;

[ApiController]
[Route("api/products")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService) => _productService = productService;

    /// <summary>商品分页查询：search（编码/名称包含）、categoryId、stockStatus=all|normal|low|out。</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProductDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] long? categoryId,
        [FromQuery] string? stockStatus,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = ProductService.DefaultPageSize,
        CancellationToken ct = default)
        => Ok(await _productService.SearchAsync(search, categoryId, stockStatus, page, pageSize, ct));

    /// <summary>商品详情（含 categoryName）。不存在 404。</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> GetById(long id, CancellationToken ct)
        => Ok(await _productService.GetByIdAsync(id, ct));

    /// <summary>新建商品（Admin）。初始库存为期初值，不产生库存交易；重复编码 409。</summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> Create(CreateProductRequest request, CancellationToken ct)
    {
        var dto = await _productService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>编辑商品（Admin）。仅允许 name/description/categoryId/unitPrice/minimumStockLevel；
    /// 请求包含 productCode/currentStock/createdAt 时返回 400。</summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> Update(long id, JsonObject body, CancellationToken ct)
    {
        var request = ParseUpdateBody(body);
        return Ok(await _productService.UpdateAsync(id, request, ct));
    }

    /// <summary>删除商品（Admin）。有库存流水或仍有正库存时返回 409 REFERENTIAL_CONFLICT。</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _productService.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>解析编辑请求体：禁止修改 productCode / currentStock / createdAt。</summary>
    private static UpdateProductRequest ParseUpdateBody(JsonObject body)
    {
        if (body is null)
            throw ApiException.Validation("Request body must be a JSON object.");

        // JSON 属性名大小写不敏感比对（契约要求包含禁止字段即返回 400）
        var keys = new HashSet<string>(body.Select(kv => kv.Key), StringComparer.OrdinalIgnoreCase);
        var forbidden = keys.Where(k => k is "productCode" or "currentStock" or "createdAt").ToList();
        if (forbidden.Count > 0)
        {
            throw ApiException.Validation(
                "Fields productCode, currentStock and createdAt cannot be modified.",
                new { forbiddenFields = forbidden });
        }

        string? GetString(string key) =>
            body.TryGetPropertyValue(key, out var value) && value is JsonValue sv && sv.TryGetValue<string>(out var s)
                ? string.IsNullOrWhiteSpace(s) ? null : s
                : null;

        long GetLong(string key, long fallback = 0) =>
            body.TryGetPropertyValue(key, out var value) && value is JsonValue lv && lv.TryGetValue<long>(out var l)
                ? l
                : fallback;

        decimal GetDecimal(string key) =>
            body.TryGetPropertyValue(key, out var value) && value is JsonValue dv && dv.TryGetValue<decimal>(out var d)
                ? d
                : 0m;

        int GetInt(string key, int fallback = 0) =>
            body.TryGetPropertyValue(key, out var value) && value is JsonValue iv && iv.TryGetValue<int>(out var i)
                ? i
                : fallback;

        return new UpdateProductRequest
        {
            Name = GetString("name") ?? string.Empty,
            Description = GetString("description"),
            CategoryId = GetLong("categoryId"),
            UnitPrice = GetDecimal("unitPrice"),
            MinimumStockLevel = GetInt("minimumStockLevel"),
        };
    }
}
