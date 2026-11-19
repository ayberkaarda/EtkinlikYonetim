using Microsoft.Data.SqlClient;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// An exclusive application lock (sp_getapplock) held by a transaction of its own connection, as another
/// application instance in the middle of a change holds it. Statements can run in that transaction while
/// the lock is held; disposing rolls it back unless it was committed, which lets the lock go.
/// </summary>
internal sealed class HeldAppLock : IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;
    private bool _ended;

    private HeldAppLock(SqlConnection connection, SqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    /// <summary>Takes the exclusive lock <paramref name="resource"/> on the database of <paramref name="database"/>.</summary>
    public static async Task<HeldAppLock> TakeAsync(SqlServerTestDatabase database, string resource, CancellationToken ct)
    {
        var connection = new SqlConnection(new SqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync(ct);
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        await using var command = new SqlCommand(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0; SELECT @r;",
            connection,
            transaction);
        command.Parameters.AddWithValue("@resource", resource);
        Assert.Equal(0, (int)(await command.ExecuteScalarAsync(ct))!);
        return new HeldAppLock(connection, transaction);
    }

    /// <summary>Runs <paramref name="sql"/> in the transaction that holds the lock.</summary>
    public async Task ExecuteAsync(string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var command = new SqlCommand(sql, _connection, _transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Commits what ran in the transaction and lets the lock go.</summary>
    public async Task CommitAsync(CancellationToken ct)
    {
        await _transaction.CommitAsync(ct);
        _ended = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_ended)
        {
            await _transaction.RollbackAsync(CancellationToken.None);
        }

        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
