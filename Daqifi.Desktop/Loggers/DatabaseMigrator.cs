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
        // Seed migration history before EF checks for pending migrations.
        // Must happen first so EF can accurately determine what's pending.
        SeedMigrationHistoryIfNeeded(databasePath);
        SqliteConnection.ClearAllPools();

        // Check if there are actually pending migrations before doing
        // the expensive backup + migrate cycle.
        if (!HasPendingMigrations(contextFactory))
        {
            return;
        }

        var backupPath = BackupDatabase(databasePath);
        CheckpointWal(databasePath);

        try
        {
            using var context = contextFactory.CreateDbContext();
            context.Database.Migrate();
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Database migration failed");
            RestoreBackup(backupPath, databasePath);
            throw;
        }

        CleanupBackup(backupPath);
        AppLogger.Instance.AddBreadcrumb("database", "Database migration completed successfully");
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// Checks whether there are any pending migrations to apply.
    /// </summary>
    private static bool HasPendingMigrations(IDbContextFactory<LoggingContext> contextFactory)
    {
        using var context = contextFactory.CreateDbContext();
        var pending = context.Database.GetPendingMigrations();
        return pending.Any();
    }

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
    /// Flushes the WAL journal into the main database file using PRAGMA wal_checkpoint.
    /// This is safer than deleting WAL/SHM files directly, which could cause data loss
    /// if uncommitted transactions exist.
    /// </summary>
    private static void CheckpointWal(string databasePath)
    {
        try
        {
            var connectionString = $"Data source={databasePath}";
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();

            connection.Close();
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Failed to checkpoint WAL — migration will proceed anyway");
        }
    }

    /// <summary>
    /// Restores the database from backup after a failed migration.
    /// </summary>
    private static void RestoreBackup(string backupPath, string databasePath)
    {
        try
        {
            if (!string.IsNullOrEmpty(backupPath) && File.Exists(backupPath))
            {
                SqliteConnection.ClearAllPools();
                File.Copy(backupPath, databasePath, overwrite: true);
                AppLogger.Instance.Error(null, "Database restored from backup after migration failure");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Failed to restore database from backup — manual recovery may be needed");
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
