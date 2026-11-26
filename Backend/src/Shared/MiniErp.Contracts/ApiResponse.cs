namespace MiniErp.Contracts;

/// <summary>
/// The one response shape every MiniErp API returns, success or failure:
/// <c>{ success, message, data, errors, traceId }</c>.
/// A record gives value equality and a concise immutable type (ideal for DTOs).
/// </summary>
public sealed record ApiResponse<T>(
    bool Success,
    string? Message,
    T? Data,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? TraceId = null)
{
    public static ApiResponse<T> Ok(T data, string? message = null) => new(true, message, data);

    public static ApiResponse<T> Fail(
        string message,
        IReadOnlyDictionary<string, string[]>? errors = null,
        string? traceId = null) => new(false, message, default, errors, traceId);
}

/// <summary>Non-generic helpers for responses without data (errors, deletes).</summary>
public static class ApiResponse
{
    public static ApiResponse<object> Fail(
        string message,
        IReadOnlyDictionary<string, string[]>? errors = null,
        string? traceId = null) => ApiResponse<object>.Fail(message, errors, traceId);

    public static ApiResponse<object> Message(string message) => new(true, message, null);
}
