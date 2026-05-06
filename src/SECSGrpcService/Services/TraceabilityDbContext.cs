using Microsoft.EntityFrameworkCore;

namespace SECSGrpcService.Services;

public sealed class TraceabilityDbContext : DbContext
{
    public TraceabilityDbContext(DbContextOptions<TraceabilityDbContext> options) : base(options)
    {
    }

    public DbSet<AlarmHistoryRecord> AlarmHistories => Set<AlarmHistoryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AlarmHistoryRecord>();
        entity.ToTable("alarm_history");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedOnAdd();
        entity.Property(x => x.Source).HasMaxLength(128).IsRequired();
        entity.Property(x => x.AlarmText).HasMaxLength(1024);
        entity.Property(x => x.AlarmCode).HasColumnType("varbinary(256)");
        entity.HasIndex(x => new { x.Source, x.AlarmId, x.OccurredAtUnixMs });
    }
}
