using System.Net;

namespace Kubernator.Core.Tests.ClusterProvisioning.Artifacts;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    public Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> Responders { get; } = new(StringComparer.Ordinal);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = request.RequestUri!.ToString();
        if (Responders.TryGetValue(key, out var responder))
        {
            return Task.FromResult(responder(request));
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
