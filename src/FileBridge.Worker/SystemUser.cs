using FileBridge.Core;

namespace FileBridge.Worker;

/// <summary>The Worker acts as the machine identity for audit trail purposes (svc:NODENAME).</summary>
public sealed class SystemUser : ICurrentUser
{
    public string Name { get; } = $"svc:{Environment.MachineName}";
}
