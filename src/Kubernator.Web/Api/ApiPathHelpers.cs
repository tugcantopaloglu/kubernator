namespace Kubernator.Web.Api;

internal static class ApiPathHelpers
{
    public static string ResolveExistingDirectory(string? raw, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw ApiException.BadRequest($"{fieldName} is required");
        }
        var resolved = Path.GetFullPath(raw);
        if (!Directory.Exists(resolved))
        {
            throw ApiException.NotFound("directory not found", resolved);
        }
        return resolved;
    }

    public static string ResolveExistingPath(string? raw, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw ApiException.BadRequest($"{fieldName} is required");
        }
        var resolved = Path.GetFullPath(raw);
        if (!Directory.Exists(resolved) && !File.Exists(resolved))
        {
            throw ApiException.NotFound("path not found", resolved);
        }
        return resolved;
    }

    public static string ResolveExistingFile(string? raw, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw ApiException.BadRequest($"{fieldName} is required");
        }
        var resolved = Path.GetFullPath(raw);
        if (!File.Exists(resolved))
        {
            throw ApiException.NotFound("file not found", resolved);
        }
        return resolved;
    }

    public static string ResolveOutputDirectory(string? raw, string fallbackName)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var resolved = Path.GetFullPath(raw);
            Directory.CreateDirectory(resolved);
            return resolved;
        }
        var temp = Path.Combine(Path.GetTempPath(), $"kubernator-{fallbackName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        return temp;
    }

    public static T RequireField<T>(T? value, string fieldName) where T : class
    {
        if (value is null)
        {
            throw ApiException.BadRequest($"{fieldName} is required");
        }
        if (value is string s && string.IsNullOrWhiteSpace(s))
        {
            throw ApiException.BadRequest($"{fieldName} is required");
        }
        return value;
    }
}
