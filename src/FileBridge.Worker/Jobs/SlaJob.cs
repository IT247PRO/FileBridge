using FileBridge.Infrastructure.Engine;
using Quartz;

namespace FileBridge.Worker.Jobs;

public sealed class SlaJob(SlaEvaluator sla) : IJob
{
    public Task Execute(IJobExecutionContext context) => sla.EvaluateAsync(context.CancellationToken);
}
