using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kubernator.Cli.Rendering;

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
