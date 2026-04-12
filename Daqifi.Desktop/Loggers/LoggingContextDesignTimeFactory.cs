using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Daqifi.Desktop.Logger;

/// <summary>
/// Factory used by EF Core tooling (dotnet ef migrations) to create
/// a LoggingContext at design time without starting the WPF application.
/// </summary>
public class LoggingContextDesignTimeFactory : IDesignTimeDbContextFactory<LoggingContext>
{
    public LoggingContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LoggingContext>();
        optionsBuilder.UseSqlite("Data source=design_time.db");
        return new LoggingContext(optionsBuilder.Options);
    }
}
