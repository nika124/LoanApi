using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LoanApi.Application.Abstractions.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LoanApi.Infrastructure.Authentication;

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public TokenResult Create(int actorId, string username, string role)
    {
        var issuedAt = timeProvider.GetUtcNow();
        var expiresAt = issuedAt.AddMinutes(_options.ExpirationMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, actorId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim(ClaimTypes.Role, role),
            new Claim("actor_type", role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            issuedAt.UtcDateTime,
            expiresAt.UtcDateTime,
            credentials);

        return new TokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAt.UtcDateTime);
    }
}
