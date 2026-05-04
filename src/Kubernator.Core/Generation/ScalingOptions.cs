namespace Kubernator.Core.Generation;

public sealed record ScalingOptions
{
    public int? HpaMinReplicas { get; init; }
    public int? HpaMaxReplicas { get; init; }
    public int? HpaTargetCpuUtilization { get; init; }
    public int? HpaTargetMemoryUtilization { get; init; }
    public int? PdbMinAvailable { get; init; }
    public int? PdbMaxUnavailable { get; init; }
    public string? PdbMinAvailablePercent { get; init; }
    public string? PdbMaxUnavailablePercent { get; init; }

    public bool HpaEnabled => HpaMinReplicas.HasValue || HpaMaxReplicas.HasValue
        || HpaTargetCpuUtilization.HasValue || HpaTargetMemoryUtilization.HasValue;

    public bool PdbEnabled => PdbMinAvailable.HasValue || PdbMaxUnavailable.HasValue
        || !string.IsNullOrEmpty(PdbMinAvailablePercent)
        || !string.IsNullOrEmpty(PdbMaxUnavailablePercent);
}
