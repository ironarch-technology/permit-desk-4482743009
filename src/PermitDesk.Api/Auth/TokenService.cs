using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PermitDesk.Api.Domain;

namespace PermitDesk.Api.Auth;

public class TokenService
{
    public const string Issuer = "permit-desk";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(10);

    private readonly SymmetricSecurityKey _key;

    public TokenService(IConfiguration configuration)
    {
        _key = KeyFrom(configuration);
    }

    public static SymmetricSecurityKey KeyFrom(IConfiguration configuration)
    {
        var secret = configuration["Auth:SigningKey"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            throw new InvalidOperationException(
                "Auth:SigningKey is not configured. Set Auth__SigningKey to a value of at least 32 characters.");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    public string Issue(User user)
    {
        var claims = new[]
        {
            new Claim("sub", user.Id.ToString()),
            new Claim("name", user.FullName),
            new Claim("role", user.Role)
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Issuer,
            claims: claims,
            expires: DateTime.UtcNow.Add(Lifetime),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
