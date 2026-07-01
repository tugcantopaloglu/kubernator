using Kubernator.Core.Deployment;

namespace Kubernator.Core.Tests.ClusterProvisioning.Fakes;

internal sealed class FakeClusterApplier : IClusterApplier
{
    public List<ClusterRegistration> Registrations { get; } = [];
    public bool RegisterResultOk { get; set; } = true;

    public Task<IReadOnlyList<ClusterContext>> ListContextsAsync(string kubectlBinary = "kubectl", CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ClusterContext>>([]);

    public Task<DeployResult> ApplyAsync(DeployOptions options, IProgress<string>? progress = null, CancellationToken ct = default) =>
        throw new NotSupportedException("not used by ClusterProvisioning tests");

    public Task<ClusterRegistrationResult> RegisterContextAsync(ClusterRegistration registration, string kubectlBinary = "kubectl", CancellationToken ct = default)
    {
        Registrations.Add(registration);
        return Task.FromResult(new ClusterRegistrationResult
        {
            Ok = RegisterResultOk,
            Errors = RegisterResultOk ? [] : ["simulated registration failure"],
            AppliedSteps = ["set-cluster: ok", "set-credentials: ok", "set-context: ok"]
        });
    }
}
