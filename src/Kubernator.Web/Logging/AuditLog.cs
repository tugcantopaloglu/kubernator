using System.Diagnostics;
using System.Text.Json;
using Kubernator.Web.Auth;

namespace Kubernator.Web.Logging;

public sealed class AuditLog : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object writeLock = new();
    private readonly string directory;
    private string currentDate = "";
    private StreamWriter? writer;

    public AuditLog() : this(ResolveDefaultDirectory()) { }

    public AuditLog(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(directory);
    }

    public static string ResolveDefaultDirectory()
    {
        var home = Environment.GetEnvironmentVariable("KUBERNATOR_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kubernator");
        return Path.Combine(home, "audit");
    }

    public void Write(AuditEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, Json);
        lock (writeLock)
        {
            EnsureWriterFor(entry.Timestamp);
            writer!.WriteLine(line);
            writer.Flush();
        }
    }

    private void EnsureWriterFor(DateTimeOffset ts)
    {
        var date = ts.ToUniversalTime().ToString("yyyyMMdd");
        if (writer is not null && date == currentDate) return;
        writer?.Dispose();
        currentDate = date;
        var path = Path.Combine(directory, $"audit-{date}.jsonl");
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        writer = new StreamWriter(stream) { AutoFlush = false };
    }

    public void Dispose()
    {
        lock (writeLock)
        {
            writer?.Dispose();
            writer = null;
        }
    }
}

public sealed record AuditEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string TraceId { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required int StatusCode { get; init; }
    public required long DurationMs { get; init; }
    public string? RemoteIp { get; init; }
    public string? UserAgent { get; init; }
    public string? KeyId { get; init; }
    public string? KeyName { get; init; }
    public string? Scope { get; init; }
    public string? AuthMethod { get; init; }
    public string? Error { get; init; }
}

internal sealed class AuditMiddleware
{
    private readonly RequestDelegate next;
    private readonly AuditLog audit;
    private readonly ILogger<AuditMiddleware> logger;

    public AuditMiddleware(RequestDelegate next, AuditLog audit, ILogger<AuditMiddleware> logger)
    {
        this.next = next;
        this.audit = audit;
        this.logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        string? error = null;
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            throw;
        }
        finally
        {
            sw.Stop();
            try
            {
                var entry = new AuditEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    TraceId = Activity.Current?.Id ?? context.TraceIdentifier,
                    Method = context.Request.Method,
                    Path = context.Request.Path.Value ?? "",
                    StatusCode = context.Response.StatusCode,
                    DurationMs = sw.ElapsedMilliseconds,
                    RemoteIp = context.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = context.Request.Headers.UserAgent.ToString(),
                    KeyId = context.User.FindFirst(ApiKeyScopes.KeyIdClaimType)?.Value,
                    KeyName = context.User.FindFirst(ApiKeyScopes.KeyNameClaimType)?.Value,
                    Scope = context.User.FindFirst(ApiKeyScopes.ScopeClaimType)?.Value,
                    AuthMethod = context.User.FindFirst("auth_method")?.Value,
                    Error = error
                };
                audit.Write(entry);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "failed to write audit entry");
            }
        }
    }
}

internal static class AuditMiddlewareExtensions
{
    public static IApplicationBuilder UseAuditLog(this IApplicationBuilder app)
        => app.UseMiddleware<AuditMiddleware>();
}
