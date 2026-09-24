using FileBridge.Core;
using FileBridge.Core.Entities;

namespace FileBridge.Infrastructure.Connectors;

public sealed class EndpointFactory(ISecretProtector secrets, IHttpClientFactory http) : IEndpointFactory
{
    public IFileEndpoint Create(Endpoint endpoint)
    {
        if (!endpoint.IsEnabled) throw new NonRetryableException($"Endpoint '{endpoint.Name}' is disabled.");
        var c = endpoint.Credential;
        var password = secrets.Unprotect(c?.ProtectedPassword);

        return endpoint.EndpointTypeId switch
        {
            EndpointType.Smb => new SmbEndpoint(endpoint, c?.Username, c?.Domain, password),
            EndpointType.LocalDisk => new SmbEndpoint(endpoint, null, null, null),
            EndpointType.Sftp => new SftpEndpoint(endpoint,
                c?.Username ?? throw new NonRetryableException($"Endpoint '{endpoint.Name}' needs a username."),
                password, secrets.Unprotect(c.ProtectedPrivateKey), secrets.Unprotect(c.ProtectedPassphrase)),
            EndpointType.Https => new HttpsEndpoint(endpoint, http.CreateClient("https-endpoint"), c?.Username, password),
            _ => throw new NonRetryableException($"Endpoint type {endpoint.EndpointTypeId} is not supported.")
        };
    }
}
