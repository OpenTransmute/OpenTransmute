using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenTransmute.Data;

/// <summary>Design-time factory so dotnet-ef migrations can create AppDbContext without the full DI stack.</summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=DB/opentransmute.db")
            .Options;
        return new AppDbContext(options);
    }
}
