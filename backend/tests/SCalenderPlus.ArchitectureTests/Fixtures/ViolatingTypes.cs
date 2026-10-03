using Microsoft.AspNetCore.Http;

// Deliberate violations used to prove that the rules detect them (see RuleSelfTests).
// They live in this test assembly only and never in production code.
namespace SCalenderPlus.ArchitectureTests.Fixtures.CoreViolation
{
    internal sealed class DomainTypeUsingHttp
    {
        public HttpContext? Context { get; set; }
    }
}

namespace SCalenderPlus.ArchitectureTests.Fixtures.ApiViolation
{
    internal sealed class EndpointCallingWorker
    {
        public Worker.Program? Worker { get; set; }
    }
}

namespace SCalenderPlus.ArchitectureTests.Fixtures.Clean
{
    internal sealed class PlainDomainType
    {
        public NodaTime.Instant At { get; set; }
    }
}
