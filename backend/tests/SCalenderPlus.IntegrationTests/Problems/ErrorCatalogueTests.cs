using System.Text.RegularExpressions;
using SCalenderPlus.Api.Problems;
using SCalenderPlus.Application.Errors;

namespace SCalenderPlus.IntegrationTests.Problems;

public sealed partial class ErrorCatalogueTests
{
    [Fact]
    public void Every_error_code_has_exactly_one_catalogue_entry()
    {
        Assert.Equal(ErrorCodes.All.Order(StringComparer.Ordinal), ProblemCatalogue.Entries.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(ErrorCodes.All.Count, ErrorCodes.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Error_codes_are_lower_snake_case()
    {
        Assert.All(ErrorCodes.All, code => Assert.Matches(SnakeCase(), code));
    }

    [Fact]
    public void Catalogue_statuses_are_errors_and_default_codes_match_their_status()
    {
        Assert.All(ProblemCatalogue.Entries.Values, e => Assert.InRange(e.Status, 400, 599));
        foreach (var status in new[] { 400, 401, 403, 404, 405, 409, 412, 413, 415, 418, 428, 429, 500, 502, 503 })
        {
            var code = ProblemCatalogue.DefaultCodeFor(status);
            var expected = status switch { 418 => 400, 502 => 500, _ => status };
            Assert.Equal(expected, ProblemCatalogue.Get(code).Status);
        }
    }

    [GeneratedRegex("^[a-z]+(_[a-z]+)*$")]
    private static partial Regex SnakeCase();
}
