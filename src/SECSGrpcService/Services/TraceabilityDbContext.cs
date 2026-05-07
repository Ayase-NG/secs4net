using Microsoft.EntityFrameworkCore;

namespace SECSGrpcService.Services;

public sealed class TraceabilityDbContext : DbContext
{
    public TraceabilityDbContext(DbContextOptions<TraceabilityDbContext> options) : base(options)
    {
    }

    public DbSet<AlarmHistoryRecord> AlarmHistories => Set<AlarmHistoryRecord>();
    public DbSet<SecsInteractionHistoryRecord> SecsInteractionHistories => Set<SecsInteractionHistoryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var alarm = modelBuilder.Entity<AlarmHistoryRecord>();
        alarm.ToTable("alarm_history");
        alarm.HasKey(x => x.Id);
        alarm.Property(x => x.Id).ValueGeneratedOnAdd();
        alarm.Property(x => x.Source).HasMaxLength(128).IsRequired();
        alarm.Property(x => x.AlarmText).HasMaxLength(1024);
        alarm.Property(x => x.AlarmCode).HasColumnType("varbinary(256)");
        alarm.HasIndex(x => new { x.Source, x.AlarmId, x.OccurredAtUnixMs });

        var interaction = modelBuilder.Entity<SecsInteractionHistoryRecord>();
        interaction.ToTable("secs_interaction_history");
        interaction.HasKey(x => x.Id);
        interaction.Property(x => x.Id).ValueGeneratedOnAdd();
        interaction.Property(x => x.SxFy).HasMaxLength(32).IsRequired();
        interaction.Property(x => x.SecsMessage).HasColumnType("longtext").IsRequired();
        interaction.HasIndex(x => new { x.SxFy, x.CreatedAtUtc });
    }
}
