using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using P03.Inventory.Api.Common;
using P03.Inventory.Api.Data;
using P03.Inventory.Api.Dtos;
using P03.Inventory.Api.Services;
using System.Security.Claims;

namespace P03.Inventory.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly SignInManager<AppUser> _signInManager;
    private readonly UserManager<AppUser> _userManager;
    private readonly ITokenService _tokenService;

    public AuthController(
        SignInManager<AppUser> signInManager,
        UserManager<AppUser> userManager,
        ITokenService tokenService)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _tokenService = tokenService;
    }

    /// <summary>登录，返回 JWT（每 IP 每分钟限流 10 次）。</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByNameAsync(request.Username);
        if (user is null)
            throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized,
                "Invalid username or password.");

        var check = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
        if (!check.Succeeded)
            throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized,
                "Invalid username or password.");

        var roles = await _userManager.GetRolesAsync(user);
        var (token, expiresAtUtc) = _tokenService.CreateToken(user, roles);
        return Ok(new LoginResponse(token, expiresAtUtc,
            new UserInfo(user.Id, user.UserName ?? string.Empty, roles.ToList())));
    }

    /// <summary>获取当前登录用户信息。</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserInfo), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserInfo>> Me(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var user = await _userManager.FindByIdAsync(userId ?? string.Empty);
        if (user is null)
            throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized,
                "User no longer exists.");

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(new UserInfo(user.Id, user.UserName ?? string.Empty, roles.ToList()));
    }
}
