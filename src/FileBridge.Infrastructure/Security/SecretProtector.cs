using FileBridge.Core;
using Microsoft.AspNetCore.DataProtection;

namespace FileBridge.Infrastructure.Security;

/// <summary>
/// Encrypts secrets with the shared Data Protection key ring (keys in tblDataProtectionKey, wrapped by an
/// X.509 certificate installed on every Admin and Worker node). Any node can decrypt; the database alone cannot.
/// </summary>
public sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("FileBridge.Secrets.v1");

    public string? Protect(string? plaintext) =>
        string.IsNullOrEmpty(plaintext) ? null : _protector.Protect(plaintext);

    public string? Unprotect(string? protectedValue) =>
        string.IsNullOrEmpty(protectedValue) ? null : _protector.Unprotect(protectedValue);
}
