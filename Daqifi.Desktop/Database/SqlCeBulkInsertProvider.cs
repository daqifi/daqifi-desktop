using EFCore.BulkExtensions;
using Microsoft.Data.Sqlite;

namespace Daqifi.Desktop
{
    public class SqliteBulkInsertProvider
    {
        public BulkInsertOptions Options { get; set; }

        // Use a method to retrieve the connection string
        protected string ConnectionString => "Data Source=|DataDirectory|\\DAQifiDatabase.sdf;Max Database Size=4091;Default Lock Timeout=10000";

        public void Run<T>(IEnumerable<T> entities)
        {
            using (var dbConnection = CreateConnection())
            {
                dbConnection.Open();

                if ((Options.SqlBulkCopyOptions & SqlBulkCopyOptions.UseInternalTransaction) > 0)
                {
                    using (var transaction = dbConnection.BeginTransaction())
                    {
                        try
                        {
                            Run(entities, dbConnection, transaction);
                            transaction.Commit();
                        }
                        catch (Exception)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
                else
                {
                    Run(entities, dbConnection, null);
                }
            }
        }


        protected SqliteConnection CreateConnection()
        {
            return new SqliteConnection(ConnectionString);
        }

        private void Run<T>(IEnumerable<T> entities, SqliteConnection connection, SqliteTransaction transaction)
        {
            bool keepNulls = (SqlBulkCopyOptions.KeepNulls & Options.SqlBulkCopyOptions) > 0;

            using (MappedDataReader<T> reader = new(entities, this))
            {
                var colInfos = ColInfos(connection, reader)
                    .Values
                    .ToArray();

                using (var cmd = CreateCommand(connection, transaction, reader.TableName))
                {
                    while (reader.Read())
                    {
                        cmd.Parameters.Clear();
                        foreach (var colInfo in colInfos)
                        {
                            var value = reader.GetValue(colInfo.ReaderKey);
                            cmd.Parameters.Add(new SqliteParameter($"@p{colInfo.OrdinalPosition}", value ?? DBNull.Value));
                        }

                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private SqliteCommand CreateCommand(SqliteConnection connection, SqliteTransaction transaction, string tableName)
        {
            var columns = string.Join(", ", ColInfos(connection, new MappedDataReader<object>(null, this)).Values.Select(c => c.OrdinalPosition));
            var parameterNames = string.Join(", ", ColInfos(connection, new MappedDataReader<object>(null, this)).Values.Select(c => $"@p{c.OrdinalPosition}"));

            var commandText = $"INSERT INTO [{tableName}] ({columns}) VALUES ({parameterNames})";

            var cmd = new SqliteCommand(commandText, connection);
            if (transaction != null)
            {
                cmd.Transaction = transaction;
            }

            return cmd;
        }

        private static Dictionary<string, ColInfo> ColInfos<T>(SqliteConnection sqlConnection, MappedDataReader<T> reader)
        {
            var colInfos = new Dictionary<string, ColInfo>();

            for (int i = 0; i < reader.Cols.Count; i++)
            {
                var colName = reader.Cols[i].ColumnName;
                colInfos[colName] = new ColInfo { OrdinalPosition = i, ReaderKey = i };
            }

            return colInfos;
        }
    }

    public class BulkInsertOptions
    {
        public SqlBulkCopyOptions SqlBulkCopyOptions { get; set; }
        public Action<object, Microsoft.Data.SqlClient.SqlRowsCopiedEventArgs> Callback { get; set; }
        public int NotifyAfter { get; set; }
    }

    public class MappedDataReader<T> : IDisposable
    {
        public Dictionary<int, ColInfo> Cols { get; set; }
        public string TableName { get; set; }

        public MappedDataReader(IEnumerable<T> entities, SqliteBulkInsertProvider provider)
        {
            // Implement mapping logic from entities to columns
        }

        public bool Read()
        {
            return false; 
        }

        public object GetValue(int key)
        {
            return null; 
        }

        public void Dispose()
        {
            // Implement disposal logic if needed
        }
    }

    public class ColInfo
    {
        public int OrdinalPosition { get; set; }
        public int ReaderKey { get; set; }
        public bool IsIdentity { get; set; }
        public string ColumnName { get; internal set; }
    }
}
