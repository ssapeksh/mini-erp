namespace Erp.Core.Common;

/*  Domain exceptions. The business layer throws these without knowing about
    HTTP; GlobalExceptionHandler in Erp.Api maps each type to a status code:
      NotFoundException        -> 404
      ConflictException        -> 409   (wrong state, duplicate, concurrency)
      BusinessRuleException    -> 422   (valid request, but a rule says no)
      ForbiddenAccessException -> 403
      FluentValidation.ValidationException -> 400                          */

public abstract class AppException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class NotFoundException(string message) : AppException(message)
{
    public static NotFoundException For(string entity, object key) => new($"{entity} '{key}' was not found.");
}

public class ConflictException(string message, Exception? inner = null) : AppException(message, inner);

/// <summary>Optimistic concurrency: the row changed since the client read it.</summary>
public sealed class ConcurrencyException(string message, Exception? inner = null) : ConflictException(message, inner);

public sealed class BusinessRuleException(string message, Exception? inner = null) : AppException(message, inner);

public sealed class ForbiddenAccessException(string message) : AppException(message);

/// <summary>Bad credentials, expired/reused refresh token, ended session -> 401.</summary>
public sealed class AuthenticationFailedException(string message) : AppException(message);
