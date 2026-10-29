using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Dtos;
using P03.Inventory.Api.Services;

namespace P03.Inventory.Api.Controllers;

/// <summary>
/// 库存流水接口（成员B）。Controller 只负责接收参数、鉴权、调用 Service、返回结果，
/// 幂等/事务/防超卖等业务逻辑全部在 StockTransactionService 中实现。
/// </summary>
[ApiController]
[Route("api/stock-transactions")]
[Authorize]
public class StockTransactionsController : ControllerBase
{
    private readonly IStockTransactionService _stockTransactionService;

    public StockTransactionsController(IStockTransactionService stockTransactionService)
        => _stockTransactionService = stockTransactionService;

    /// <summary>创建库存事务。权限：Operator 允许 IN/OUT；Admin 允许 IN/OUT/ADJUST（E14）。
    /// 首次成功 201；相同 RequestId 相同负载重放返回 200 + 首次结果；不同负载 409 IDEMPOTENCY_CONFLICT。</summary>
    [HttpPost]
    [ProducesResponseType(typeof(StockTransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(StockTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockTransactionDto>> Create(
        CreateStockTransactionRequest request, CancellationToken ct)
    {
        // 鉴权：ADJUST 仅限 Admin；Operator 只允许 IN/OUT
        var type = request.Type?.Trim().ToUpperInvariant();
        if (type == StockTransactionService.TypeAdjust && !User.IsInRole("Admin"))
        {
            throw new ApiException(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden,
                "Only administrators can perform ADJUST transactions.");
        }

        // 操作人取自 JWT（sub → NameIdentifier），写入流水 ActorUserId 供审计
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized,
                "Authentication required. Provide a valid bearer token.");

        var (dto, created) = await _stockTransactionService.CreateAsync(request, actorUserId, ct);

        // 首次创建 201 Created；幂等重放 200 OK + 首次成功结果（不重复扣减/入库）
        return created
            ? StatusCode(StatusCodes.Status201Created, dto)
            : Ok(dto);
    }

    /// <summary>流水分页查询（登录即可）：productId、type、fromUtc（含）、toUtc（不含），
    /// 按 CreatedAt DESC, Id DESC 排序。</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<StockTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<StockTransactionDto>>> Query(
        [FromQuery] long? productId,
        [FromQuery] string? type,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = StockTransactionService.DefaultPageSize,
        CancellationToken ct = default)
        => Ok(await _stockTransactionService.QueryAsync(
            productId, type, fromUtc, toUtc, page, pageSize, ct));
}
