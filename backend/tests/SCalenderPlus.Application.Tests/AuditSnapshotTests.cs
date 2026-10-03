using System.Text.Json.Nodes;
using SCalenderPlus.Application.Auditing;

namespace SCalenderPlus.Application.Tests;

public sealed class AuditSnapshotTests
{
    private sealed record Credentials(string UserName, [property: Sensitive] string Pin, string PasswordHash, string? RecoveryCodes);

    [Fact]
    public void Null_state_is_null()
    {
        Assert.Null(AuditSnapshot.Serialize(null));
    }

    [Fact]
    public void Plain_state_is_camel_case_json()
    {
        var json = JsonNode.Parse(AuditSnapshot.Serialize(new { Name = "Lions", MemberCount = 3 })!)!;

        Assert.Equal("Lions", (string?)json["name"]);
        Assert.Equal(3, (int?)json["memberCount"]);
    }

    [Fact]
    public void Sensitive_attribute_and_secret_like_names_are_redacted_null_stays_null()
    {
        var json = JsonNode.Parse(AuditSnapshot.Serialize(new Credentials("mia", "1234", "AQAAAA…", null))!)!;

        Assert.Equal("mia", (string?)json["userName"]);
        Assert.Equal(AuditSnapshot.Redacted, (string?)json["pin"]);
        Assert.Equal(AuditSnapshot.Redacted, (string?)json["passwordHash"]);
        Assert.Null(json["recoveryCodes"]);
    }

    [Fact]
    public void Nested_objects_arrays_and_dictionaries_are_redacted()
    {
        var state = new
        {
            Webhook = new { Url = "https://example.test/hook", SigningSecret = "whsec_1" },
            Tokens = new[] { new { Name = "cli", Token = "scal_pat_x" } },
            Headers = new Dictionary<string, string> { ["api_key"] = "k", ["accept"] = "json" },
        };

        var serialized = AuditSnapshot.Serialize(state)!;

        Assert.DoesNotContain("whsec_1", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("scal_pat_x", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"k\"", serialized, StringComparison.Ordinal);
        Assert.Contains("https://example.test/hook", serialized, StringComparison.Ordinal);
        Assert.Contains("\"accept\":\"json\"", serialized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("password", true)]
    [InlineData("newPassword", true)]
    [InlineData("security_stamp", true)]
    [InlineData("totpSecret", true)]
    [InlineData("feedTokenId", true)]
    [InlineData("displayName", false)]
    [InlineData("timeZone", false)]
    public void Secret_like_names(string name, bool sensitive)
    {
        Assert.Equal(sensitive, AuditSnapshot.IsSensitiveName(name));
    }
}
