using Npgsql;

namespace HeadlessScreenReader.Worker;

/// <summary>Shared trusted coordinator configuration; no connection value is returned to a reader child.</summary>
internal static class WorkerDatabase
{
    internal static NpgsqlDataSource Create(string connectionString)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            IncludeErrorDetail = false,
            LogParameters = false,
            CommandTimeout = 10,
            Timeout = 10,
            MaxPoolSize = 3,
            ApplicationName = "hsr-observation-worker"
        };
        var loopback = settings.Host is "localhost" or "127.0.0.1" or "::1";
        if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Password)
            || !loopback && settings.SslMode != SslMode.VerifyFull)
            throw new ArgumentException("WORKER_DATABASE_CONFIGURATION_INVALID");
        return NpgsqlDataSource.Create(settings.ConnectionString);
    }
}
