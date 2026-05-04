namespace Kubernator.Core.Diagnostics;

public interface IDiagnosticsService
{
    Task<DiagnosticReport> RunAsync(CancellationToken ct = default);
}
