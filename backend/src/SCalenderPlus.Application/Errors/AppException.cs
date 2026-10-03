namespace SCalenderPlus.Application.Errors;

/// <summary>
/// An expected failure of a use case with a stable <see cref="ErrorCodes">error code</see>. The Api maps it to
/// an RFC 9457 problem response (status and title from the problem catalogue); <see cref="Detail"/> is shown
/// to the user and <see cref="Extensions"/> become additional problem members (e.g. <c>limit</c>,
/// <c>required</c>). Handlers may also return problem results directly; throwing is for deep call stacks.
/// </summary>
public sealed class AppException : Exception
{
    public AppException(string code, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null)
        : base(detail ?? code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Detail = detail;
        Extensions = extensions ?? new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    public AppException()
        : this(ErrorCodes.InternalError)
    {
    }

    public AppException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = ErrorCodes.InternalError;
        Extensions = new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    public string Code { get; }

    public string? Detail { get; }

    public IReadOnlyDictionary<string, object?> Extensions { get; }
}
