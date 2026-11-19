namespace EtkinlikYonetim.Business;

/// <summary>
/// Raised when an operation violates a business rule. The message is user facing (Turkish), fixed
/// and free of entered values, so it can be shown to the user and written to a log as is.
/// </summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException()
    {
    }

    public BusinessRuleException(string message)
        : base(message)
    {
    }

    public BusinessRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when the record being changed was changed by somebody else after it was loaded; nothing was
/// written. The caller shows the message and loads the record again, so the change can be made on the
/// current values.
/// </summary>
public sealed class StaleDataException : BusinessRuleException
{
    public StaleDataException()
    {
    }

    public StaleDataException(string message)
        : base(message)
    {
    }

    public StaleDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when an open session may no longer be used: its account was deleted, disabled or given
/// another role since the sign-in. The check reads the database at the time of the operation, so a
/// demoted or disabled admin cannot keep working in a window that is still open. The user has to sign
/// in again.
/// </summary>
public sealed class SessionExpiredException : Exception
{
    internal const string DefaultMessage = "Oturumunuz artık geçerli değil. Lütfen yeniden giriş yapın.";

    public SessionExpiredException()
        : base(DefaultMessage)
    {
    }

    public SessionExpiredException(string message)
        : base(message)
    {
    }

    public SessionExpiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when the signed-in account tries to see or change data that does not belong to it. The
/// message is fixed and does not reveal whether the requested record exists.
/// </summary>
public sealed class AccessDeniedException : Exception
{
    internal const string DefaultMessage = "Bu işlem için yetkiniz yok.";

    public AccessDeniedException()
        : base(DefaultMessage)
    {
    }

    public AccessDeniedException(string message)
        : base(message)
    {
    }

    public AccessDeniedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
