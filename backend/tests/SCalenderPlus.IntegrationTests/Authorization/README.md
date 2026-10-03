# Authorization matrix

Every operation of the OpenAPI document (`/openapi/v1.json`, i.e. every endpoint with OpenAPI metadata) must be covered here before it can be merged ([workflow.md §6](../../../../docs/development/workflow.md#6-testing-strategy), issue #26).

| File | Role |
|---|---|
| `AuthorizationMatrix.cs` | The matrix: `AnonymousOperations` (deliberately public, with a reason) and `Cases` (operation × actor → expected status). |
| `MatrixActor.cs` | Actors: build an `HttpClient` for an identity in the scenario — `Anonymous`, `User` (verified), `UnverifiedUser`, `OtherUser` (cross-tenant), `FreshSession` (signed in anew per case, for logout). |
| `MatrixScenario.cs` | Class fixture: migrated database + api host, seeded users with one signed-in session (cookie jar) per actor, seeded resources addressed by name (`scenario.Get("group:lions")`, `scenario.Get("email:user")`, `scenario.Get("user:user")`). |
| `MatrixCoverage.cs` | Completeness rules (pure function, unit-tested). |
| `AuthorizationMatrixCoverageTests.cs` | Enumerates the operations of the **served** OpenAPI document and fails for every operation without entries. No Docker. |
| `AuthorizationMatrixTests.cs` | Runs every case (and every anonymous operation) against the real api + PostgreSQL (`Category=Docker`). |

## Rules (enforced by `MatrixCoverage`)

1. Each operation is either in `AnonymousOperations` or has at least one case — not both.
2. A protected operation has an `anonymous → 401` case.
3. An operation with path parameters has a cross-tenant case (`actor.IsCrossTenant`) expecting `404` (no existence leaks), and every case supplies route values (`WithRoute`).
4. No entries for operations that are not in the document (renamed/removed endpoints), and at most one case per operation and actor.

## Adding an endpoint

```csharp
// AuthorizationMatrix.Cases
.. For("PATCH", "/api/v1/groups/{id}")
    .WithRoute(s => new Dictionary<string, string> { ["id"] = s.Get("group:lions") })
    .WithBody(_ => JsonContent.Create(new { name = "Lions FC" }))
    .Expect(Actors.Anonymous, HttpStatusCode.Unauthorized)
    .Expect(Actors.OtherTenant, HttpStatusCode.NotFound)
    .Expect(Actors.GroupMember, HttpStatusCode.Forbidden)
    .Expect(Actors.GroupAdmin, HttpStatusCode.OK),
```

- The expected status is the one for a **valid** request: send a valid body so a `400` cannot mask a missing authorization check. Requests carry `X-Requested-With: scal` (CSRF has its own tests).
- Mutating cases share the class's scenario; prefer cases whose effect does not change other cases' outcome (e.g. rename instead of delete), or seed a dedicated resource per destructive case.
- New actors go to `Actors` (M1-C: group owner/admin/member/non-member; M2: calendar and event levels from [permissions.md](../../../../docs/architecture/permissions.md)); their users/sessions and the resources they relate to are created in `MatrixScenario.SeedAsync`. Signed-in actors share one session per test run (`scenario.SessionClient(name)`, cookie updates are kept); a case that ends or replaces its session uses a fresh one (`scenario.NewSessionClientAsync(name)`, see `Actors.FreshSession`).
- Public operations (auth endpoints such as `register`/`login`, invite acceptance by token, iCal feeds) go to `AnonymousOperations` with a reason and a valid body; the test asserts they answer without authentication (never 401/403) with a 2xx, or with the given `expected` status (e.g. `400` for token-based operations: the token in the body is the credential).
