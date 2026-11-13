using System.Security.Cryptography;
using System.Text;
using PermitDesk.Api.Integrations;

namespace PermitDesk.Tests;

public class WebhookSignatureTests
{
    private const string Secret = "test-secret";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"handle\":\"zc-1\",\"result\":\"pass\"}");

    private static string Sign(byte[] body, string secret) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();

    [Fact]
    public void Accepts_a_correct_signature()
    {
        Assert.True(WebhookSignature.IsValid(Sign(Body, Secret), Body, Secret));
    }

    [Fact]
    public void Rejects_a_signature_made_with_another_secret()
    {
        Assert.False(WebhookSignature.IsValid(Sign(Body, "other"), Body, Secret));
    }

    [Fact]
    public void Rejects_a_changed_body()
    {
        var tampered = Encoding.UTF8.GetBytes("{\"handle\":\"zc-1\",\"result\":\"fail\"}");
        Assert.False(WebhookSignature.IsValid(Sign(Body, Secret), tampered, Secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256=not-hex")]
    [InlineData("md5=abcdef")]
    public void Rejects_missing_or_malformed_headers(string? header)
    {
        Assert.False(WebhookSignature.IsValid(header, Body, Secret));
    }
}
