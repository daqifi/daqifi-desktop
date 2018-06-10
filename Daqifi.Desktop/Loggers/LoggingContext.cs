using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using System;
using System.Data.Entity;

namespace Daqifi.Desktop.Logger
{
    [Serializable]
    public class LoggingContext : DbContext
    {
        #region Constructors
        public LoggingContext() : base("name=LoggingDatabaseContext")
        {
            try
            {
                Database.SetInitializer(new CreateDatabaseIfNotExists<LoggingContext>());
            }
            catch(Exception ex)
            {
                AppLogger.Instance.Error(ex, "Error Logging to Database");
            }
        }
        #endregion

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LoggingSession>()
                .HasMany(c => c.DataSamples)
                .WithRequired(p => p.LoggingSession)
                .WillCascadeOnDelete(true);
            base.OnModelCreating(modelBuilder);
        }

        #region DBSets
        public DbSet<LoggingSession> Sessions { get; set; }
        public DbSet<DataSample> Samples { get; set; }
        #endregion
    }
}