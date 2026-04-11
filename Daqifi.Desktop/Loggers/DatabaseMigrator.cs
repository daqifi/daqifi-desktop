using System.IO;
using Daqifi.Desktop.Common.Loggers;
using Microsoft.Data.Sqlite;
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
        var backupPath = BackupDatabase(databasePath);
        SeedMigrationHistoryIfNeeded(databasePath);

        // Clear pooled connections and stale WAL files to prevent lock conflicts
        SqliteConnection.ClearAllPools();
        CleanupWalFiles(databasePath);

        using var context = contextFactory.CreateDbContext();
        context.Database.Migrate();

        CleanupBackup(backupPath);
        AppLogger.Instance.AddBreadcrumb("database", "Database migration completed successfully");
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// Creates a backup copy of the SQLite database file before applying migrations.
    /// </summary>
    /// <returns>The backup file path, or null if no backup was created.</returns>
    private static string BackupDatabase(string databasePath)
    {
        try
        {
            if (!File.Exists(databasePath))
            {
                return null;
            }

            var backupPath = databasePath + ".migration-backup";
            File.Copy(databasePath, backupPath, overwrite: true);
            return backupPath;
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Failed to back up database before migration");
            return null;
        }
    }

    /// <summary>
    /// Removes the backup file after a successful migration.
    /// </summary>
    private static void CleanupBackup(string backupPath)
    {
        try
        {
            if (!string.IsNullOrEmpty(backupPath) && File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Failed to delete migration backup file");
        }
    }

    /// <summary>
    /// Removes WAL and SHM files that can hold stale locks from previous sessions.
    /// </summary>
    private static void CleanupWalFiles(string databasePath)
    {
        try
        {
            var walPath = databasePath + "-wal";
            var shmPath = databasePath + "-shm";

            if (File.Exists(walPath))
            {
                File.Delete(walPath);
            }

            if (File.Exists(shmPath))
            {
                File.Delete(shmPath);
            }
        }
        catch
        {
            // Non-critical — WAL files will be recreated by SQLite
        }
    }

    /// <summary>
    /// For databases created by <c>EnsureCreated()</c>, the <c>__EFMigrationsHistory</c>
    /// table does not exist. This method creates it and seeds the initial migration entry
    /// so that <c>Migrate()</c> only applies subsequent migrations.
    /// Uses a raw <see cref="SqliteConnection"/> to avoid EF connection pooling locks.
    /// </summary>
    private static void SeedMigrationHistoryIfNeeded(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return;
        }

        var connectionString = $"Data source={databasePath}";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        if (HasMigrationHistoryTable(connection))
        {
            connection.Close();
            return;
        }

        if (!HasExistingTables(connection))
        {
            connection.Close();
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" " +
            "(\"MigrationId\" TEXT NOT NULL PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL); " +
            "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" " +
            $"VALUES ('{INITIAL_MIGRATION_ID}', '{EF_PRODUCT_VERSION}');";
        command.ExecuteNonQuery();

        connection.Close();
    }

    /// <summary>
    /// Checks whether the <c>__EFMigrationsHistory</c> table exists in the database.
    /// </summary>
    private static bool HasMigrationHistoryTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='table' AND name='__EFMigrationsHistory'";
        var result = command.ExecuteScalar();
        return result is long count && count > 0;
    }

    /// <summary>
    /// Checks whether the database has existing application tables (created by <c>EnsureCreated()</c>).
    /// </summary>
    private static bool HasExistingTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='table' AND name='Samples'";
        var result = command.ExecuteScalar();
        return result is long count && count > 0;
    }
    #endregion
}
