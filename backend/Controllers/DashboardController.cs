using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Dtos;
using P03.Inventory.Api.Services;

namespace P03.Inventory.Api.Controllers;

/// <summary>仪表盘接口（成员B）。纯只读查询，Controller 不含业务逻辑。</summary>
[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService) => _dashboardService = dashboardService;

    /// <summary>仪表盘：4 项核心指标 + 最近 10 条流水 + 过去 7 个 UTC 自然日净变更（零填充）。</summary>
    [HttpGet]
    [ProducesResponseType(typeof(DashboardDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardDto>> Get(CancellationToken ct)
        => Ok(await _dashboardService.GetAsync(ct));
}
