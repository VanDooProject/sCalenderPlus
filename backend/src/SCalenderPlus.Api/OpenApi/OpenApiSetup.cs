using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.OpenApi;

namespace SCalenderPlus.Api.OpenApi;

/// <summary>
/// OpenAPI 3.1 document <c>v1</c>, served at <c>/openapi/v1.json</c> and exported at build time to
/// <c>backend/openapi/v1.json</c> (docs/architecture/api.md §5).
/// </summary>
internal static class OpenApiSetup
{
    public const string DocumentName = "v1";

    public static IServiceCollection AddApiDocument(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                // Fixed metadata only: no assembly version or server URLs, so the committed document only
                // changes when the contract does (release version bumps must not cause spec drift).
                document.Info = new OpenApiInfo
                {
                    Title = "sCalenderPlus API",
                    Version = DocumentName,
                    Description = "Public REST API of sCalenderPlus. See docs/architecture/api.md.",
                };
                document.Servers = [];
                return Task.CompletedTask;
            });
        });

    public static IEndpointRouteBuilder MapApiDocument(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOpenApi("/openapi/{documentName}.json");
        return endpoints;
    }
}

/// <summary>
/// Build-time document generation (<c>Microsoft.Extensions.ApiDescription.Server</c>) starts the app inside
/// the <c>GetDocument.Insider</c> tool. Required options are validated on start, so it gets placeholder values;
/// nothing connects to them because no request is served.
/// </summary>
internal static class BuildTimeDocument
{
    public static bool IsGenerating =>
        string.Equals(System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name, "GetDocument.Insider", StringComparison.Ordinal);

    public static readonly IReadOnlyDictionary<string, string?> PlaceholderSettings = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["App:PublicBaseUrl"] = "https://openapi.invalid",
        ["ConnectionStrings:Default"] = "Host=openapi.invalid;Database=openapi",
        ["Database:AutoMigrate"] = "false",
    };

    /// <summary>
    /// Keeps the Data Protection key ring in memory: the database placeholder is unreachable and the key ring
    /// is preloaded on start, which would only log connection errors (and the unencrypted-key warning) into the
    /// build output.
    /// </summary>
    public static IServiceCollection UseInMemoryKeyRing(this IServiceCollection services) =>
        services.Configure<KeyManagementOptions>(o =>
        {
            o.XmlRepository = new InMemoryXmlRepository();
            o.XmlEncryptor = new NullXmlEncryptor();
        });

    private sealed class InMemoryXmlRepository : IXmlRepository
    {
        private readonly List<XElement> _elements = [];

        public IReadOnlyCollection<XElement> GetAllElements()
        {
            lock (_elements)
            {
                return [.. _elements.Select(e => new XElement(e))];
            }
        }

        public void StoreElement(XElement element, string friendlyName)
        {
            lock (_elements)
            {
                _elements.Add(new XElement(element));
            }
        }
    }
}
