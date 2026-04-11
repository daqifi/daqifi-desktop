using System.IO;
using Daqifi.Desktop.Common.Loggers;
using Microsoft.EntityFrameworkCore;

namespace Daqifi.Desktop.Logger;

/// <summary>
/// Handles database migration at application startup, including upgrade path
/// for existing databases created by <c>EnsureCreated()</c>.
/// </summary>
public static class DatabaseMigrator
{
    #region Constants
    private const string INITIAL_MIGRATION_ID = "20250812090000_InitialSQLiteMigration";
    private const string EF_PRODUCT_VERSION = "9.0.14";
    #endregion

    #region Public Methods
    /// <summary>
    /// Applies pending EF Core migrations. For existing databases created by
    /// <c>EnsureCreated()</c> (which lack a <c>__EFMigrationsHistory</c> table),
    /// seeds the migration history first so <c>Migrate()</c> does not attempt
    /// to recreate existing tables.
    /// </summary>
    /// <param name="contextFactory">The DbContext factory registered in DI.</param>
    /// <param name="databasePath">Full path to the SQLite database file.</param>
    public static void MigrateDatabase(IDbContextFactory<LoggingContext> contextFactory, string databasePath)
    {
        BackupDatabase(databasePath);
        SeedMigrationHistoryIfNeeded(contextFactory);

        using var context = contextFactory.CreateDbContext();
        context.Database.Migrate();

        AppLogger.Instance.AddBreadcrumb("database", "Database migration completed successfully");
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// Creates a backup copy of the SQLite database file before applying migrations.
    /// </summary>
    private static void BackupDatabase(string databasePath)
    {
        try
        {
            if (!File.Exists(databasePath))
            {
                return;
            }

            var backupPath = databasePath + ".backup";
            File.Copy(databasePath, backupPath, overwrite: true);
            AppLogger.Instance.AddBreadcrumb("database", $"Database backed up to {backupPath}");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Failed to back up database before migration");
        }
    }

    /// <summary>
    /// For databases created by <c>EnsureCreated()</c>, the <c>__EFMigrationsHistory</c>
    /// table does not exist. This method creates it and seeds the initial migration entry
    /// so that <c>Migrate()</c> only applies subsequent migrations.
    /// Uses a separate context that is fully disposed before Migrate() runs.
    /// </summary>
    private static void SeedMigrationHistoryIfNeeded(IDbContextFactory<LoggingContext> contextFactory)
    {
        using var context = contextFactory.CreateDbContext();

        if (HasMigrationHistoryTable(context))
        {
            return;
        }

        if (!HasExistingTables(context))
        {
            return;
        }

        AppLogger.Instance.AddBreadcrumb("database",
            "Existing database detected without migration history — seeding initial migration");

        context.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" " +
            "(\"MigrationId\" TEXT NOT NULL PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL)");

        context.Database.ExecuteSqlRaw(
            "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" " +
            $"VALUES ('{INITIAL_MIGRATION_ID}', '{EF_PRODUCT_VERSION}')");
    }

    /// <summary>
    /// Checks whether the <c>__EFMigrationsHistory</c> table exists in the database.
    /// </summary>
    private static bool HasMigrationHistoryTable(LoggingContext context)
    {
        try
        {
            var result = context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS \"Value\" FROM sqlite_master " +
                "WHERE type='table' AND name='__EFMigrationsHistory'").ToList();
            return result.Count > 0 && result[0] > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether the database has existing application tables (created by <c>EnsureCreated()</c>).
    /// </summary>
    private static bool HasExistingTables(LoggingContext context)
    {
        try
        {
            var result = context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS \"Value\" FROM sqlite_master " +
                "WHERE type='table' AND name='Samples'").ToList();
            return result.Count > 0 && result[0] > 0;
        }
        catch
        {
            return false;
        }
    }
    #endregion
}
