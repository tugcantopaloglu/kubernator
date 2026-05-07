using System.Diagnostics;
using System.Text.Json;

namespace Kubernator.Web.Api;

internal sealed class ApiExceptionMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate next;
    private readonly ILogger<ApiExceptionMiddleware> logger;
    private readonly IHostEnvironment environment;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger, IHostEnvironment environment)
    {
        this.next = next;
        this.logger = logger;
        this.environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = 499;
            }
            return;
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        if (context.Response.HasStarted)
        {
            logger.LogError(ex, "exception after response started; cannot rewrite. {Path}", context.Request.Path);
            throw ex;
        }

        var problem = MapException(ex, context);

        if (problem.Status >= 500)
        {
            logger.LogError(ex, "unhandled exception {Method} {Path} traceId={TraceId}", context.Request.Method, context.Request.Path, problem.TraceId);
        }
        else
        {
            logger.LogWarning("api {StatusCode} {Method} {Path} traceId={TraceId} error={Error}",
                problem.Status, context.Request.Method, context.Request.Path, problem.TraceId, problem.Title);
        }

        context.Response.Clear();
        context.Response.StatusCode = problem.Status;
        context.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(context.Response.Body, problem, JsonOptions);
    }

    private ApiProblem MapException(Exception ex, HttpContext context)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        var instance = context.Request.Path.Value;

        return ex switch
        {
            ApiException api => new ApiProblem
            {
                Type = $"https://kubernator/errors/{api.Error.Replace(' ', '-')}",
                Title = api.Error,
                Status = api.StatusCode,
                Detail = api.Detail,
                Instance = instance,
                TraceId = traceId,
                Errors = api.Errors
            },
            ArgumentException => new ApiProblem
            {
                Type = "https://kubernator/errors/invalid-argument",
                Title = "invalid argument",
                Status = StatusCodes.Status400BadRequest,
                Detail = ex.Message,
                Instance = instance,
                TraceId = traceId
            },
            FileNotFoundException fnf => new ApiProblem
            {
                Type = "https://kubernator/errors/not-found",
                Title = "not found",
                Status = StatusCodes.Status404NotFound,
                Detail = environment.IsDevelopment() ? ex.Message : fnf.FileName,
                Instance = instance,
                TraceId = traceId
            },
            DirectoryNotFoundException => new ApiProblem
            {
                Type = "https://kubernator/errors/not-found",
                Title = "not found",
                Status = StatusCodes.Status404NotFound,
                Detail = ex.Message,
                Instance = instance,
                TraceId = traceId
            },
            UnauthorizedAccessException => new ApiProblem
            {
                Type = "https://kubernator/errors/forbidden",
                Title = "forbidden",
                Status = StatusCodes.Status403Forbidden,
                Detail = ex.Message,
                Instance = instance,
                TraceId = traceId
            },
            NotSupportedException => new ApiProblem
            {
                Type = "https://kubernator/errors/not-supported",
                Title = "not supported",
                Status = StatusCodes.Status422UnprocessableEntity,
                Detail = ex.Message,
                Instance = instance,
                TraceId = traceId
            },
            InvalidOperationException => new ApiProblem
            {
                Type = "https://kubernator/errors/conflict",
                Title = "operation not permitted in current state",
                Status = StatusCodes.Status409Conflict,
                Detail = ex.Message,
                Instance = instance,
                TraceId = traceId
            },
            JsonException => new ApiProblem
            {
                Type = "https://kubernator/errors/invalid-json",
                Title = "invalid json payload",
                Status = StatusCodes.Status400BadRequest,
                Detail = ex.Message,
                Instance = instance,
                TraceId = traceId
            },
            BadHttpRequestException bad => new ApiProblem
            {
                Type = "https://kubernator/errors/bad-request",
                Title = "bad request",
                Status = bad.StatusCode,
                Detail = bad.Message,
                Instance = instance,
                TraceId = traceId
            },
            _ => new ApiProblem
            {
                Type = "https://kubernator/errors/internal",
                Title = "internal server error",
                Status = StatusCodes.Status500InternalServerError,
                Detail = environment.IsDevelopment() ? ex.ToString() : "an unexpected error occurred",
                Instance = instance,
                TraceId = traceId
            }
        };
    }
}

internal static class ApiExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseApiExceptionHandler(this IApplicationBuilder app)
        => app.UseMiddleware<ApiExceptionMiddleware>();
}
