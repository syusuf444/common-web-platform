using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Platform.Api.Data;

public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PlatformDb")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=CommonWebPlatform;Integrated Security=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new PlatformDbContext(options);
    }
}
