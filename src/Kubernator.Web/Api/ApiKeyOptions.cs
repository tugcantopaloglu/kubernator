namespace Kubernator.Web.Api;

public sealed class ApiKeyOptions
{
    public const string HeaderName = "X-Api-Key";
    public const string SchemeName = "ApiKey";

    public string? BootstrapKey { get; init; }

    public bool IsConfigured => !string.IsNullOrEmpty(BootstrapKey);
}
