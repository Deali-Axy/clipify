using Clipify.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clipify.Persistence;

public sealed class ClipifyDbContext : DbContext
{
    public ClipifyDbContext(DbContextOptions<ClipifyDbContext> options)
        : base(options)
    {
    }

    public DbSet<MediaJobEntity> MediaJobs => Set<MediaJobEntity>();
    public DbSet<MediaArtifactEntity> MediaArtifacts => Set<MediaArtifactEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new MediaJobEntityConfiguration());
        modelBuilder.ApplyConfiguration(new MediaArtifactEntityConfiguration());
    }
}

file sealed class MediaJobEntityConfiguration : IEntityTypeConfiguration<MediaJobEntity>
{
    public void Configure(EntityTypeBuilder<MediaJobEntity> builder)
    {
        builder.ToTable("media_jobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.Property(x => x.State).HasMaxLength(32).IsRequired();
        builder.Property(x => x.DefinitionKind).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DefinitionJson).IsRequired();
        builder.Property(x => x.RetryOfJobId).HasMaxLength(64);
        builder.Property(x => x.WorkflowId).HasMaxLength(64);
        builder.Property(x => x.ParentJobId).HasMaxLength(64);
        builder.Property(x => x.Stage).HasMaxLength(128);
        builder.Property(x => x.ErrorCode).HasMaxLength(64);
        builder.Property(x => x.LeaseOwner).HasMaxLength(128);
        builder.Property(x => x.LogPath).HasMaxLength(1024);

        builder.HasIndex(x => new { x.State, x.Priority, x.CreatedAtUnixMs, x.Id })
            .HasDatabaseName("ix_media_jobs_queue");
        builder.HasIndex(x => x.LeaseExpiresAtUnixMs)
            .HasDatabaseName("ix_media_jobs_lease_expires");
        builder.HasIndex(x => x.CreatedAtUnixMs)
            .HasDatabaseName("ix_media_jobs_created");
    }
}

file sealed class MediaArtifactEntityConfiguration : IEntityTypeConfiguration<MediaArtifactEntity>
{
    public void Configure(EntityTypeBuilder<MediaArtifactEntity> builder)
    {
        builder.ToTable("media_artifacts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.Property(x => x.JobId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Path).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(128);

        builder.HasIndex(x => x.JobId).HasDatabaseName("ix_media_artifacts_job");
        builder.HasOne(x => x.Job)
            .WithMany(x => x.Artifacts)
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
