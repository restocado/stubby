using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stubby.Infrastructure.Persistence;

namespace Stubby.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<StubbyDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Stubby")));
        return services;
    }
}
