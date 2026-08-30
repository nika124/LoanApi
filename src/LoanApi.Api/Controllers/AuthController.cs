using LoanApi.Application.DTOs;
using LoanApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoanApi.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("users/register")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Register(
        RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authService.RegisterUserAsync(request, cancellationToken);
        return CreatedAtAction(nameof(UsersController.GetById), "Users", new { id = response.Id }, response);
    }

    [AllowAnonymous]
    [HttpPost("users/login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> LoginUser(
        LoginRequest request,
        CancellationToken cancellationToken) =>
        Ok(await authService.LoginUserAsync(request, cancellationToken));

    [AllowAnonymous]
    [HttpPost("accountants/login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> LoginAccountant(
        LoginRequest request,
        CancellationToken cancellationToken) =>
        Ok(await authService.LoginAccountantAsync(request, cancellationToken));
}
