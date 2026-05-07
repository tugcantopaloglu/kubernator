namespace Kubernator.Web.Api;

public sealed class ApiKeyOptions
{
    public const string HeaderName = "X-Api-Key";
    public const string SchemeName = "ApiKey";
    public const string PolicyName = "ApiKey";

    public string? ApiKey { get; init; }

    public bool IsConfigured => !string.IsNullOrEmpty(ApiKey);
}
