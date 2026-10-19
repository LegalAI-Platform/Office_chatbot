using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Office.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef migrations add`. Never connects to a real database.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OfficeDbContext>
{
    public OfficeDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OfficeDbContext>()
            .UseNpgsql("Host=design-time-only;Database=office",
                n => n.MigrationsHistoryTable(OfficeDbContext.MigrationsHistoryTable, OfficeDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options);
}
