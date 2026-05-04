using Kubernator.Core.Generation;

namespace Kubernator.Cli.Infrastructure;

internal static class ScalingBuilder
{
    public static ScalingOptions? Build(
        int? hpaMin,
        int? hpaMax,
        int? hpaCpu,
        int? hpaMemory,
        int? pdbMinAvailable,
        int? pdbMaxUnavailable,
        string? pdbMinAvailablePercent,
        string? pdbMaxUnavailablePercent)
    {
        var anyHpa = hpaMin.HasValue || hpaMax.HasValue || hpaCpu.HasValue || hpaMemory.HasValue;
        var anyPdb = pdbMinAvailable.HasValue || pdbMaxUnavailable.HasValue
            || !string.IsNullOrEmpty(pdbMinAvailablePercent)
            || !string.IsNullOrEmpty(pdbMaxUnavailablePercent);

        if (!anyHpa && !anyPdb)
        {
            return null;
        }

        return new ScalingOptions
        {
            HpaMinReplicas = hpaMin,
            HpaMaxReplicas = hpaMax,
            HpaTargetCpuUtilization = hpaCpu,
            HpaTargetMemoryUtilization = hpaMemory,
            PdbMinAvailable = pdbMinAvailable,
            PdbMaxUnavailable = pdbMaxUnavailable,
            PdbMinAvailablePercent = pdbMinAvailablePercent,
            PdbMaxUnavailablePercent = pdbMaxUnavailablePercent
        };
    }
}
