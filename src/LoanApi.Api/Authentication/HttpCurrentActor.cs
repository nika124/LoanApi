using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LoanApi.Application.Abstractions.CurrentUser;

namespace LoanApi.Api.Authentication;

public sealed class HttpCurrentActor(IHttpContextAccessor httpContextAccessor) : ICurrentActor
{
    private ClaimsPrincipal Principal => httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    public int Id => int.TryParse(Principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : 0;

    public string Role => Principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
}
