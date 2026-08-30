using LoanApi.Application.DTOs;
using LoanApi.Application.Services;
using LoanApi.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoanApi.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = ApplicationRoles.UserOrAccountant)]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class UsersController(IUserService userService) : ControllerBase
{
    [HttpGet("{id:int}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> GetById(int id, CancellationToken cancellationToken) =>
        Ok(await userService.GetByIdAsync(id, cancellationToken));

    [HttpPost("{id:int}/blocks")]
    [Authorize(Roles = ApplicationRoles.Accountant)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Block(
        int id,
        BlockUserRequest request,
        CancellationToken cancellationToken)
    {
        await userService.BlockAsync(id, request, cancellationToken);
        return NoContent();
    }
}
