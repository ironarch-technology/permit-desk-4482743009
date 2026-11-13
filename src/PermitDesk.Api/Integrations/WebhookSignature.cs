using System.Security.Cryptography;
using System.Text;

namespace PermitDesk.Api.Integrations;

/// <summary>
/// City services sign webhook bodies with HMAC-SHA256 and send the digest as
/// "X-Signature: sha256=&lt;hex&gt;".
/// </summary>
public static class WebhookSignature
{
    public const string Header = "X-Signature";

    public static bool IsValid(string? header, byte[] body, string secret)
    {
        if (string.IsNullOrEmpty(header) || !header.StartsWith("sha256=", StringComparison.Ordinal)) return false;

        byte[] presented;
        try
        {
            presented = Convert.FromHexString(header["sha256=".Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}
