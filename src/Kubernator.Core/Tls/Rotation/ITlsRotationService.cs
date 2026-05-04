namespace Kubernator.Core.Tls.Rotation;

public interface ITlsRotationService
{
    Task<TlsRotationResult> GenerateAsync(TlsRotationOptions options, CancellationToken ct = default);
}
