using Microsoft.Data.SqlClient;

namespace RitmoGuatemalaMVC.Data;

public sealed class ReadOnlySqlConnectionFactory
{
    public const string PasswordEnvironmentVariable = "RITMOGUATE_SQL_PASSWORD";
    public const string DatabaseName = "db_RitmoGuate";

    public SqlConnection Create()
    {
        var password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                $"La variable de entorno {PasswordEnvironmentVariable} es necesaria para conectarse a SQL Server.");
        }

        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = "localhost,1435",
            InitialCatalog = DatabaseName,
            UserID = "sa",
            Password = password,
            Encrypt = true,
            TrustServerCertificate = true,
            ApplicationIntent = ApplicationIntent.ReadOnly,
            ConnectTimeout = 5,
            PersistSecurityInfo = false
        };

        return new SqlConnection(connectionString.ConnectionString);
    }
}
