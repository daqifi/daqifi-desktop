using Daqifi.Desktop.Channel;
using Microsoft.EntityFrameworkCore;

namespace Daqifi.Desktop.Logger
{
    public class LoggingContext : DbContext
    {
        public LoggingContext(DbContextOptions<LoggingContext> options) : base(options)
        {
            Database.EnsureCreated();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LoggingSession>().HasMany(c => c.DataSamples).WithOne(p => p.LoggingSession).IsRequired().OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<LoggingSession>().Property(ls => ls.Name).IsRequired();
            modelBuilder.Entity<Channel.Channel>().Ignore(c => c.ChannelColorBrush);
            base.OnModelCreating(modelBuilder);
        }

        public DbSet<LoggingSession> Sessions { get; set; }
        public DbSet<DataSample> Samples { get; set; }
    }
}