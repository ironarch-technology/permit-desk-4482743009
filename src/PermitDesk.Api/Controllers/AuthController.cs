using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Auth;
using PermitDesk.Api.Data;
using PermitDesk.Api.Domain;
using PermitDesk.Api.Models;

namespace PermitDesk.Api.Controllers;

[Route("api/auth")]
public class AuthController : PortalController
{
    private readonly PermitDeskDbContext _db;
    private readonly TokenService _tokens;
    private readonly IPasswordHasher<User> _hasher;

    public AuthController(PermitDeskDbContext db, TokenService tokens, IPasswordHasher<User> hasher)
    {
        _db = db;
        _tokens = tokens;
        _hasher = hasher;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return Unauthorized(new { error = "Email or password is incorrect." });
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user == null ||
            _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { error = "Email or password is incorrect." });
        }

        return Ok(new
        {
            token = _tokens.Issue(user),
            expiresInSeconds = (int)TokenService.Lifetime.TotalSeconds,
            user = new { id = user.Id, name = user.FullName, role = user.Role }
        });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == CurrentUserId, ct);
        if (user == null) return Unauthorized();

        return Ok(new { id = user.Id, name = user.FullName, email = user.Email, phone = user.Phone, role = user.Role });
    }
}
