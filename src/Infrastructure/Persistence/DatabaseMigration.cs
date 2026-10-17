using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Stubby.Infrastructure.Persistence;

public static class DatabaseMigration
{
    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StubbyDbContext>();
        await db.Database.MigrateAsync();
    }
}
