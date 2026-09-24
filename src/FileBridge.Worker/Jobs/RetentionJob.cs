using FileBridge.Infrastructure.Engine;
using Quartz;

namespace FileBridge.Worker.Jobs;

public sealed class RetentionJob(RetentionService retention) : IJob
{
    public Task Execute(IJobExecutionContext context) => retention.RunAsync(context.CancellationToken);
}
