using Daqifi.Desktop.Channel;
using Microsoft.EntityFrameworkCore;

namespace Daqifi.Desktop.Logger;

public class LoggingContext : DbContext
{
    public LoggingContext(DbContextOptions<LoggingContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LoggingSession>(entity =>
        {
            entity.ToTable("Sessions");
            entity.HasMany(c => c.DataSamples).WithOne(p => p.LoggingSession).IsRequired().OnDelete(DeleteBehavior.Cascade);
            entity.Property(ls => ls.Name).IsRequired();
        });

        modelBuilder.Entity<DataSample>(entity =>
        {
            entity.ToTable("Samples");
            entity.HasIndex(s => new { s.LoggingSessionID, s.TimestampTicks })
                .HasDatabaseName("IX_Samples_SessionTime");
        });

        modelBuilder.Entity<Channel.Channel>(entity =>
        {
            entity.ToTable("Channel");
            entity.Ignore(c => c.ChannelColorBrush);
        });

        base.OnModelCreating(modelBuilder);
    }

    public DbSet<LoggingSession> Sessions { get; set; }
    public DbSet<DataSample> Samples { get; set; }
}
