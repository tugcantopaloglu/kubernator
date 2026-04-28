using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Kubernator.Core.Analysis.DotNet;

internal sealed record AssemblyMetadata
{
    public required string AssemblyName { get; init; }
    public required string Version { get; init; }
    public bool HasEntryPoint { get; init; }
    public IReadOnlyList<string> ReferencedAssemblies { get; init; } = [];
}

internal static class AssemblyMetadataReader
{
    public static AssemblyMetadata Read(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);

        if (!pe.HasMetadata)
        {
            throw new InvalidOperationException($"PE file has no managed metadata: {assemblyPath}");
        }

        var reader = pe.GetMetadataReader();
        var asm = reader.GetAssemblyDefinition();
        var name = reader.GetString(asm.Name);
        var version = asm.Version.ToString();

        var refs = new List<string>(reader.AssemblyReferences.Count);
        foreach (var handle in reader.AssemblyReferences)
        {
            var asmRef = reader.GetAssemblyReference(handle);
            refs.Add(reader.GetString(asmRef.Name));
        }

        var hasEntryPoint = pe.PEHeaders.CorHeader is { EntryPointTokenOrRelativeVirtualAddress: not 0 };

        return new AssemblyMetadata
        {
            AssemblyName = name,
            Version = version,
            HasEntryPoint = hasEntryPoint,
            ReferencedAssemblies = refs
        };
    }
}
