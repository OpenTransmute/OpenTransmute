using Microsoft.EntityFrameworkCore;
using OpenTransmute.Models;

namespace OpenTransmute.Data;

/// <summary>
/// EF Core database context for OpenTransmute.
/// Manages <see cref="DecomposedProject"/> and <see cref="InventoryItem"/> entities
/// in a SQLite database.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    #region Properties

    public DbSet<DecomposedProject> Projects => Set<DecomposedProject>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<DecomposePhaseOutput> PhaseOutputs => Set<DecomposePhaseOutput>();
    public DbSet<SavedComposeJob> ComposeJobs => Set<SavedComposeJob>();
    public DbSet<SavedImplementJob> ImplementJobs => Set<SavedImplementJob>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    #endregion

    #region Methods

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DecomposedProject>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).IsRequired();
            e.Property(p => p.Source).IsRequired();
            e.Property(p => p.OutputPath).IsRequired();
            e.HasMany(p => p.Items)
             .WithOne(i => i.Project)
             .HasForeignKey(i => i.ProjectId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InventoryItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Name).IsRequired();
            e.Property(i => i.Summary).IsRequired();
            e.Property(i => i.DetailsJson).IsRequired();
            e.Property(i => i.RawMarkdown).IsRequired();
            e.Property(i => i.SecurityNotes).IsRequired();
        });

        modelBuilder.Entity<SavedComposeJob>(e =>
        {
            e.HasKey(j => j.Id);
            e.Property(j => j.OutputName).IsRequired();
            e.Property(j => j.Output).IsRequired();
            e.Property(j => j.OrchestratorType).IsRequired();
            e.Property(j => j.LogLinesJson).IsRequired();
        });

        modelBuilder.Entity<DecomposePhaseOutput>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Filename).IsRequired();
            e.Property(o => o.Content).IsRequired();
            e.HasOne(o => o.Project)
             .WithMany(p => p.PhaseOutputs)
             .HasForeignKey(o => o.ProjectId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SavedImplementJob>(e =>
        {
            e.HasKey(j => j.Id);
            e.Property(j => j.Label).IsRequired();
            e.Property(j => j.OutputDirectory).IsRequired();
            e.Property(j => j.OrchestratorType).IsRequired();
            e.Property(j => j.LogLinesJson).IsRequired();
        });

        modelBuilder.Entity<UserSettings>(e =>
        {
            e.HasKey(u => u.Id);
        });
    }

    #endregion
}
