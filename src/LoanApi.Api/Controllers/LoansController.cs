using LoanApi.Application.DTOs;
using LoanApi.Application.Services;
using LoanApi.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoanApi.Api.Controllers;

[ApiController]
[Route("api/loans")]
[Authorize(Roles = ApplicationRoles.UserOrAccountant)]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class LoansController(ILoanService loanService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<LoanResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LoanResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await loanService.ListAsync(cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType<LoanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoanResponse>> GetById(int id, CancellationToken cancellationToken) =>
        Ok(await loanService.GetByIdAsync(id, cancellationToken));

    [HttpGet("users/{userId:int}")]
    [ProducesResponseType<IReadOnlyList<LoanResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<LoanResponse>>> ListForUser(
        int userId,
        CancellationToken cancellationToken) =>
        Ok(await loanService.ListForUserAsync(userId, cancellationToken));

    [HttpPost]
    [Authorize(Roles = ApplicationRoles.User)]
    [ProducesResponseType<LoanResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoanResponse>> Create(
        CreateLoanRequest request,
        CancellationToken cancellationToken)
    {
        var response = await loanService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = ApplicationRoles.User)]
    [ProducesResponseType<LoanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LoanResponse>> UpdateOwn(
        int id,
        UpdateOwnLoanRequest request,
        CancellationToken cancellationToken) =>
        Ok(await loanService.UpdateOwnAsync(id, request, cancellationToken));

    [HttpPatch("{id:int}")]
    [Authorize(Roles = ApplicationRoles.Accountant)]
    [ProducesResponseType<LoanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoanResponse>> UpdateAsAccountant(
        int id,
        AccountantUpdateLoanRequest request,
        CancellationToken cancellationToken) =>
        Ok(await loanService.UpdateAsAccountantAsync(id, request, cancellationToken));

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await loanService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:int}/history")]
    [Authorize(Roles = ApplicationRoles.Accountant)]
    [ProducesResponseType<IReadOnlyList<LoanHistoryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<LoanHistoryResponse>>> History(
        int id,
        CancellationToken cancellationToken) =>
        Ok(await loanService.GetHistoryAsync(id, cancellationToken));
}
