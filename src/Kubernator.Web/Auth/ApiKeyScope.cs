namespace Kubernator.Web.Auth;

public enum ApiKeyScope
{
    Read = 0,
    Generate = 1,
    Admin = 2
}

public static class ApiKeyScopes
{
    public const string ReadPolicy = "ApiKeyRead";
    public const string GeneratePolicy = "ApiKeyGenerate";
    public const string AdminPolicy = "ApiKeyAdmin";
    public const string DownloadPolicy = "DownloadCookieOrApiKey";

    public const string ScopeClaimType = "kubernator_scope";
    public const string KeyIdClaimType = "kubernator_key_id";
    public const string KeyNameClaimType = "kubernator_key_name";

    public static bool Allows(ApiKeyScope held, ApiKeyScope required) => held >= required;

    public static bool TryParse(string? raw, out ApiKeyScope scope)
    {
        if (Enum.TryParse(raw, ignoreCase: true, out scope))
        {
            return Enum.IsDefined(scope);
        }
        scope = ApiKeyScope.Read;
        return false;
    }
}
