using System.Diagnostics;
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
        Log("MigrateDatabase started");

        BackupDatabase(databasePath);

        // Seed migration history using raw ADO.NET to avoid EF connection
        // pooling issues that can leave locks blocking Migrate().
        SeedMigrationHistoryIfNeeded(databasePath);

        // Clear any pooled connections before Migrate() to prevent lock conflicts
        Log("Clearing SQLite connection pool");
        SqliteConnection.ClearAllPools();

        // Delete WAL/SHM files that may hold stale locks
        CleanupWalFiles(databasePath);

        Log("Creating context for Migrate()");
        using var context = contextFactory.CreateDbContext();

        Log("Calling Database.Migrate()");
        var sw = Stopwatch.StartNew();
        context.Database.Migrate();
        sw.Stop();
        Log($"Database.Migrate() completed in {sw.Elapsed.TotalSeconds:F1}s");

        AppLogger.Instance.AddBreadcrumb("database", "Database migration completed successfully");
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// Writes a debug message to both Debug output and AppLogger.
    /// </summary>
    private static void Log(string message)
    {
        Debug.WriteLine($"[DatabaseMigrator] {message}");
        AppLogger.Instance.AddBreadcrumb("database", message);
    }

    /// <summary>
    /// Creates a backup copy of the SQLite database file before applying migrations.
    /// </summary>
    private static void BackupDatabase(string databasePath)
    {
        try
        {
            if (!File.Exists(databasePath))
            {
                Log("No database file found — skipping backup");
                return;
            }

            var backupPath = databasePath + ".backup";
            Log($"Backing up database ({new FileInfo(databasePath).Length / 1024 / 1024}MB)");
            File.Copy(databasePath, backupPath, overwrite: true);
            Log("Backup complete");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Failed to back up database before migration");
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
                Log($"Deleting stale WAL file ({new FileInfo(walPath).Length} bytes)");
                File.Delete(walPath);
            }

            if (File.Exists(shmPath))
            {
                Log("Deleting stale SHM file");
                File.Delete(shmPath);
            }
        }
        catch (Exception ex)
        {
            Log($"Warning: could not clean WAL files: {ex.Message}");
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
            Log("No database file — skipping seed check");
            return;
        }

        Log("Opening raw connection for seed check");
        var connectionString = $"Data source={databasePath}";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        if (HasMigrationHistoryTable(connection))
        {
            Log("Migration history table already exists — skipping seed");
            connection.Close();
            return;
        }

        if (!HasExistingTables(connection))
        {
            Log("No existing tables found — fresh database, skipping seed");
            connection.Close();
            return;
        }

        Log("Existing database without migration history — seeding initial migration");

        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" " +
            "(\"MigrationId\" TEXT NOT NULL PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL); " +
            "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" " +
            $"VALUES ('{INITIAL_MIGRATION_ID}', '{EF_PRODUCT_VERSION}');";
        command.ExecuteNonQuery();

        Log("Seed complete — closing raw connection");
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
