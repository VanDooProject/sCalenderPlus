using SCalenderPlus.Application.Jobs;

namespace SCalenderPlus.IntegrationTests.Jobs;

internal sealed class DelegateJobHandler(string type, Func<JobContext, CancellationToken, Task> handle) : IJobHandler
{
    public string Type => type;

    public Task HandleAsync(JobContext context, CancellationToken cancellationToken) => handle(context, cancellationToken);
}
