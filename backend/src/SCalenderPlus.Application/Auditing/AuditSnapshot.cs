using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace SCalenderPlus.Application.Auditing;

/// <summary>
/// Serializes before/after states for <c>audit_events</c> (camelCase JSON). Secrets never reach the audit log:
/// properties marked <see cref="SensitiveAttribute"/> and any member whose name looks like a secret
/// (password, token, secret, hash, API/private key, recovery code, security stamp, TOTP) are replaced by
/// <see cref="Redacted"/>, also inside nested objects, arrays and dictionaries.
/// </summary>
public static class AuditSnapshot
{
    public const string Redacted = "[redacted]";

    private static readonly string[] _sensitiveNameParts =
    [
        "password", "secret", "token", "hash", "apikey", "privatekey", "recoverycode", "securitystamp", "totp", "authenticatorkey",
    ];

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { RedactSensitiveAttribute } },
    };

    public static string? Serialize(object? state)
    {
        if (state is null)
        {
            return null;
        }

        var node = JsonSerializer.SerializeToNode(state, state.GetType(), _json);
        RedactByName(node);
        return node?.ToJsonString() ?? "null";
    }

    public static bool IsSensitiveName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalized = name.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return _sensitiveNameParts.Any(part => normalized.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    private static void RedactByName(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj.ToList())
                {
                    if (IsSensitiveName(name))
                    {
                        obj[name] = value is null ? null : Redacted;
                    }
                    else
                    {
                        RedactByName(value);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    RedactByName(item);
                }

                break;
        }
    }

    private static void RedactSensitiveAttribute(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        for (var i = 0; i < typeInfo.Properties.Count; i++)
        {
            var property = typeInfo.Properties[i];
            if (property.AttributeProvider?.IsDefined(typeof(SensitiveAttribute), inherit: true) != true)
            {
                continue;
            }

            var get = property.Get;
            var replacement = typeInfo.CreateJsonPropertyInfo(typeof(string), property.Name);
            replacement.Get = owner => get?.Invoke(owner) is null ? null : Redacted;
            typeInfo.Properties[i] = replacement;
        }
    }
}
