namespace Kubernator.Web.Api;

public sealed class ApiException : Exception
{
    public ApiException(int statusCode, string error, string? detail = null, IDictionary<string, string[]>? errors = null)
        : base(detail ?? error)
    {
        StatusCode = statusCode;
        Error = error;
        Detail = detail;
        Errors = errors;
    }

    public int StatusCode { get; }
    public string Error { get; }
    public string? Detail { get; }
    public IDictionary<string, string[]>? Errors { get; }

    public static ApiException BadRequest(string error, string? detail = null)
        => new(StatusCodes.Status400BadRequest, error, detail);

    public static ApiException NotFound(string error, string? detail = null)
        => new(StatusCodes.Status404NotFound, error, detail);

    public static ApiException Conflict(string error, string? detail = null)
        => new(StatusCodes.Status409Conflict, error, detail);

    public static ApiException Unprocessable(string error, string? detail = null)
        => new(StatusCodes.Status422UnprocessableEntity, error, detail);
}
