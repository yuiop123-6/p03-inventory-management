using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Dtos;
using P03.Inventory.Api.Services;

namespace P03.Inventory.Api.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    public CategoriesController(ICategoryService categoryService) => _categoryService = categoryService;

    /// <summary>分类列表，按 Id 升序。</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CategoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CategoryDto>>> GetAll(CancellationToken ct)
    {
        var items = await _categoryService.GetAllAsync(ct);
        return Ok(PagedResult<CategoryDto>.Create(items, items.Count, page: 1, pageSize: items.Count));
    }

    /// <summary>新建分类（Admin）。名称 Trim，忽略大小写重复返回 409。</summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryDto>> Create(CreateCategoryRequest request, CancellationToken ct)
    {
        var dto = await _categoryService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetAll), new { id = dto.Id }, dto);
    }

    /// <summary>更新分类（Admin）。不存在 404，重复 409。</summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryDto>> Update(long id, UpdateCategoryRequest request, CancellationToken ct)
        => Ok(await _categoryService.UpdateAsync(id, request, ct));

    /// <summary>删除分类（Admin）。有关联商品返回 409 REFERENTIAL_CONFLICT。</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _categoryService.DeleteAsync(id, ct);
        return NoContent();
    }
}
