using Microsoft.Data.SqlClient;

namespace EtkinlikYonetim.Data.Persistence;

/// <summary>What a database error means for the user, without its message.</summary>
public static class DatabaseErrors
{
    /// <summary>SQL Server's error number of a transaction chosen as a deadlock victim.</summary>
    public const int DeadlockVictimNumber = 1205;

    /// <summary>
    /// Whether <paramref name="exception"/> or one of its inner exceptions says SQL Server chose the
    /// statement as a deadlock victim. Its transaction was rolled back and nothing was written, so the
    /// operation can simply be tried again, like one that found the event's lock busy.
    /// </summary>
    public static bool IsDeadlock(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: DeadlockVictimNumber })
            {
                return true;
            }

            if (current is AggregateException { InnerExceptions.Count: > 0 } aggregate && IsDeadlock(aggregate.InnerExceptions[0]))
            {
                return true;
            }
        }

        return false;
    }
}
