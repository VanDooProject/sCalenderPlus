using System.Collections.Frozen;
using Microsoft.Extensions.Options;

namespace SCalenderPlus.Application.Accounts;

/// <summary>
/// Decides whether an email address may be used to sign up: disposable-email providers (bundled list
/// <c>disposable-email-domains.txt</c>) and operator-configured domains are rejected, including their
/// subdomains. Free accounts are a cost vector (LLM imports), so throwaway sign-ups are refused up front.
/// </summary>
public sealed class EmailDomainPolicy
{
    private const string ResourceName = "SCalenderPlus.Application.Accounts.disposable-email-domains.txt";

    private static readonly Lazy<List<string>> _bundled = new(LoadBundled);

    private readonly FrozenSet<string> _blocked;

    public EmailDomainPolicy(IOptions<SignUpOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        var configured = (value.BlockedEmailDomains ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _blocked = (value.BlockDisposableEmailDomains ? _bundled.Value : [])
            .Concat(configured)
            .Select(Normalize)
            .ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>The bundled disposable-email domains.</summary>
    public static IReadOnlyList<string> BundledDomains => _bundled.Value;

    /// <summary>True when the address's domain (or a parent domain) is blocked.</summary>
    public bool IsBlocked(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        var at = email.LastIndexOf('@');
        if (at < 0)
        {
            return false;
        }

        var domain = Normalize(email[(at + 1)..]);
        while (domain.Length > 0)
        {
            if (_blocked.Contains(domain))
            {
                return true;
            }

            var dot = domain.IndexOf('.', StringComparison.Ordinal);
            domain = dot < 0 ? string.Empty : domain[(dot + 1)..];
        }

        return false;
    }

    private static string Normalize(string domain) => domain.Trim().TrimEnd('.').ToLowerInvariant();

    private static List<string> LoadBundled()
    {
        using var stream = typeof(EmailDomainPolicy).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        var domains = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                domains.Add(line);
            }
        }

        return domains;
    }
}
