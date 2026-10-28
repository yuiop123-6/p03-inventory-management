using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Dtos;
using P03.Inventory.Api.Services;

namespace P03.Inventory.Api.Controllers;

/// <summary>报表接口（成员B）。纯只读查询，Controller 不含业务逻辑。</summary>
[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportsController(IReportService reportService) => _reportService = reportService;

    /// <summary>商品库存报表：categoryId、stockStatus、search 过滤 + 分页，含库存金额（unitPrice × currentStock）。</summary>
    [HttpGet("products")]
    [ProducesResponseType(typeof(PagedResult<ReportProductDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ReportProductDto>>> Products(
        [FromQuery] string? search,
        [FromQuery] long? categoryId,
        [FromQuery] string? stockStatus,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = ReportService.DefaultPageSize,
        CancellationToken ct = default)
        => Ok(await _reportService.ProductReportAsync(search, categoryId, stockStatus, page, pageSize, ct));

    /// <summary>历史流水报表：productId、type、fromUtc（含）、toUtc（不含）过滤 + 分页，默认最新优先。</summary>
    [HttpGet("transactions")]
    [ProducesResponseType(typeof(PagedResult<StockTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<StockTransactionDto>>> Transactions(
        [FromQuery] long? productId,
        [FromQuery] string? type,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = ReportService.DefaultPageSize,
        CancellationToken ct = default)
        => Ok(await _reportService.TransactionReportAsync(productId, type, fromUtc, toUtc, page, pageSize, ct));
}
