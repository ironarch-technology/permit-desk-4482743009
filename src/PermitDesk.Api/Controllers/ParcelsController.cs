using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PermitDesk.Api.Data;

namespace PermitDesk.Api.Controllers;

[Authorize]
[Route("api/parcels")]
public class ParcelsController : PortalController
{
    private readonly PermitDeskDbContext _db;

    public ParcelsController(PermitDeskDbContext db)
    {
        _db = db;
    }

    /// <summary>Address suggestions for the intake form.</summary>
    [HttpGet("search")]
    [EnableRateLimiting("intake")]
    public async Task<IActionResult> Search([FromQuery] string? q, CancellationToken ct)
    {
        var term = q?.Trim() ?? "";
        if (term.Length < 3) return Ok(Array.Empty<object>());

        var matches = new List<object>();
        var connection = _db.Database.GetDbConnection();
        await _db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();

            // Addresses that start with the term rank above ones that only contain it.
            command.CommandText = $@"
                SELECT parcel_number, address, district
                  FROM parcels
                 WHERE address ILIKE '%{term}%'
                 ORDER BY (address ILIKE '{term}%') DESC, address
                 LIMIT 8";

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                matches.Add(new
                {
                    parcelNumber = reader.GetString(0),
                    address = reader.GetString(1),
                    district = reader.GetString(2)
                });
            }
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }

        return Ok(matches);
    }

    [HttpGet("resolve")]
    [EnableRateLimiting("intake")]
    public async Task<IActionResult> Resolve([FromQuery] string? address, CancellationToken ct)
    {
        var lowered = address?.Trim().ToLower() ?? "";
        var parcel = await _db.Parcels.AsNoTracking().FirstOrDefaultAsync(p => p.Address.ToLower() == lowered, ct);
        if (parcel == null) return NotFound(new { error = "We could not find that address in Mountport parcel records." });

        return Ok(new { parcelNumber = parcel.ParcelNumber, address = parcel.Address, district = parcel.District });
    }
}
