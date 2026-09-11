namespace OmsLoan.Data.Postgres;

/// <summary>
/// Config values for Database:Provider. SQL Server is the default when the key is missing or blank.
/// A typo here throws at registration time — under an installed Windows service that is a restart
/// loop, not the clean refusal StartupValidation gives for missing settings.
/// </summary>
public static class DatabaseProvider
{
    public const string ConfigurationKey = "Database:Provider";

    public const string SqlServer = "SqlServer";

    public const string Postgres = "Postgres";
}
