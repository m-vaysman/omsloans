using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace LoanDbModel
{
    /// <summary>
    /// Refuses a database that still has the March 2025 <c>loan</c> schema.
    /// Creating the current <c>loans</c> schema beside it would leave the app
    /// reading empty tables.
    /// </summary>
    public static class LegacyLoanSchema
    {
        public const string RefuseSql = """
            IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'loan')
                THROW 50001, N'This database still has the old loan schema. Refusing to treat an empty loans schema as the data. Move or drop schema loan before updating.', 1;

            DECLARE @schema sysname, @sql nvarchar(max), @found int;
            DECLARE history CURSOR LOCAL FAST_FORWARD FOR
                SELECT s.name
                FROM sys.tables AS t
                INNER JOIN sys.schemas AS s ON t.schema_id = s.schema_id
                WHERE t.name = N'__EFMigrationsHistory';
            OPEN history;
            FETCH NEXT FROM history INTO @schema;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @sql = N'SELECT @foundOut = CASE WHEN EXISTS (SELECT 1 FROM '
                    + QUOTENAME(@schema) + N'.[__EFMigrationsHistory] WHERE MigrationId LIKE ''20250331%'' OR MigrationId LIKE ''20250401%'') THEN 1 ELSE 0 END';
                EXEC sys.sp_executesql @sql, N'@foundOut int OUTPUT', @foundOut = @found OUTPUT;
                IF @found = 1
                BEGIN
                    CLOSE history;
                    DEALLOCATE history;
                    THROW 50001, N'This database still records the March 2025 loan migrations. Refusing to create an empty loans schema beside that history.', 1;
                END
                FETCH NEXT FROM history INTO @schema;
            END
            CLOSE history;
            DEALLOCATE history;
            """;
    }

    public sealed class LegacyLoanSchemaGuard : DbConnectionInterceptor
    {
        public static readonly LegacyLoanSchemaGuard Instance = new();

        private int verified;

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            ThrowIfLegacy(connection);
        }

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            ThrowIfLegacy(connection);
            return Task.CompletedTask;
        }

        private void ThrowIfLegacy(DbConnection connection)
        {
            if (Volatile.Read(ref verified) == 1)
            {
                return;
            }

            using var command = connection.CreateCommand();
            command.CommandText = LegacyLoanSchema.RefuseSql;
            command.ExecuteNonQuery();
            Interlocked.Exchange(ref verified, 1);
        }
    }
}
