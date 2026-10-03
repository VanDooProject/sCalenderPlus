using System.Net;

namespace SCalenderPlus.IntegrationTests.Authorization;

/// <summary>The completeness rules of the matrix, as a pure function so they can be tested on their own.</summary>
public static class MatrixCoverage
{
    public static IReadOnlyList<string> Check(
        IReadOnlyCollection<ApiOperation> documentOperations,
        IReadOnlyCollection<AnonymousOperation> anonymousOperations,
        IReadOnlyCollection<MatrixCase> cases)
    {
        ArgumentNullException.ThrowIfNull(documentOperations);
        ArgumentNullException.ThrowIfNull(anonymousOperations);
        ArgumentNullException.ThrowIfNull(cases);

        var problems = new List<string>();
        var inDocument = documentOperations.ToHashSet();
        var anonymous = anonymousOperations.Select(a => a.Operation).ToHashSet();
        var byOperation = cases.ToLookup(c => c.Operation);

        foreach (var operation in documentOperations.OrderBy(o => o.Path, StringComparer.Ordinal).ThenBy(o => o.Method, StringComparer.Ordinal))
        {
            var protectedCases = byOperation[operation].ToList();
            if (anonymous.Contains(operation))
            {
                if (protectedCases.Count > 0)
                {
                    problems.Add($"{operation}: listed as anonymous but also has protected cases; choose one.");
                }

                continue;
            }

            if (protectedCases.Count == 0)
            {
                problems.Add($"{operation}: no authorization matrix case. Add cases to AuthorizationMatrix.Cases "
                    + "(or, if it is deliberately public, to AuthorizationMatrix.AnonymousOperations with a reason).");
                continue;
            }

            if (!protectedCases.Any(c => c.Actor == Actors.Anonymous && c.Expected == HttpStatusCode.Unauthorized))
            {
                problems.Add($"{operation}: missing case 'anonymous -> 401'.");
            }

            if (operation.HasPathParameters && !protectedCases.Any(c => c.Actor.IsCrossTenant && c.Expected == HttpStatusCode.NotFound))
            {
                problems.Add($"{operation}: has path parameters but no cross-tenant case expecting 404 (tenant isolation).");
            }

            if (operation.HasPathParameters && protectedCases.Any(c => c.RouteValues is null))
            {
                problems.Add($"{operation}: has path parameters; every case needs route values (WithRoute).");
            }
        }

        foreach (var stale in anonymous.Concat(cases.Select(c => c.Operation)).Distinct().Where(o => !inDocument.Contains(o)))
        {
            problems.Add($"{stale}: matrix entry for an operation that is not in the OpenAPI document (removed or renamed?).");
        }

        foreach (var duplicate in cases.GroupBy(c => (c.Operation, c.Actor)).Where(g => g.Count() > 1))
        {
            problems.Add($"{duplicate.Key.Operation}: more than one case for actor '{duplicate.Key.Actor.Name}'.");
        }

        return problems;
    }
}
