using Microsoft.EntityFrameworkCore;
using Stubby.Domain.Receipts;

namespace Stubby.Infrastructure.Persistence;

public sealed class StubbyDbContext(DbContextOptions<StubbyDbContext> options) : DbContext(options)
{
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<FieldRecord> FieldRecords => Set<FieldRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StubbyDbContext).Assembly);
}
