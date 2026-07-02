using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kubernator.Web.Jobs;

internal static class JobJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
