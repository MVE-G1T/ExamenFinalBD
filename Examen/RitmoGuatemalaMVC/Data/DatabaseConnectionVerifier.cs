using System.Data;
using Microsoft.Data.SqlClient;

namespace RitmoGuatemalaMVC.Data;

public sealed class DatabaseConnectionVerifier(
    ReadOnlySqlConnectionFactory connectionFactory,
    ILogger<DatabaseConnectionVerifier> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(ReadOnlySqlConnectionFactory.PasswordEnvironmentVariable)))
        {
            logger.LogWarning(
                "SQL connection check skipped: environment variable {EnvironmentVariable} is not set.",
                ReadOnlySqlConnectionFactory.PasswordEnvironmentVariable);
            return;
        }

        try
        {
            await using var connection = connectionFactory.Create();
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(
                "SELECT DB_NAME() WHERE DB_NAME() = @ExpectedDatabaseName;",
                connection);
            command.Parameters.Add("@ExpectedDatabaseName", SqlDbType.NVarChar, 128)
                .Value = ReadOnlySqlConnectionFactory.DatabaseName;

            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is not string databaseName)
            {
                logger.LogError("SQL connection check did not return the expected database.");
                return;
            }

            logger.LogInformation(
                "Read-only SQL connection verified for database {DatabaseName}.",
                databaseName);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "SQL connection verification failed ({ExceptionType}); the application will remain available.",
                exception.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
