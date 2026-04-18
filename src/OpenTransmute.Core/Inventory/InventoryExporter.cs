using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OpenTransmute.Data;

namespace OpenTransmute.Inventory;

/// <summary>
/// Exports inventory data from the database to JSON files on disk.
/// Used after a decompose run completes to produce a portable snapshot of the inventory.
/// </summary>
public class InventoryExporter(IDbContextFactory<AppDbContext> dbFactory, ILogger<InventoryExporter> logger)
{
    #region Members

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    #endregion

    #region Methods

    /// <summary>
    /// Exports all inventory items for a single project to <c>&lt;outputDir&gt;/inventory.json</c>.
    /// Silently returns if the project does not exist in the database.
    /// </summary>
    /// <param name="projectName">The project to export.</param>
    /// <param name="outputDir">Directory where <c>inventory.json</c> will be written.</param>
    /// <param name="ct">Cancellation token propagated from the caller.</param>
    public async Task ExportProjectAsync(string projectName, string outputDir, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        Models.DecomposedProject? project = await db.Projects
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Name == projectName, ct);

        if (project is null) return;

        object payload = BuildPayload(project);
        string path = Path.Combine(outputDir, "inventory.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, JsonOptions), ct);
        logger.LogInformation("Exported inventory for {Project} to {Path}", projectName, path);
    }

    /// <summary>
    /// Exports all projects to <c>&lt;outputRoot&gt;/inventory-export.json</c>.
    /// </summary>
    /// <param name="outputRoot">Root directory where <c>inventory-export.json</c> will be written.</param>
    /// <param name="ct">Cancellation token propagated from the caller.</param>
    public async Task ExportAllAsync(string outputRoot, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        List<Models.DecomposedProject> projects = await db.Projects.Include(p => p.Items).ToListAsync(ct);

        List<object> payload = projects.Select(BuildPayload).ToList();
        string path = Path.Combine(outputRoot, "inventory-export.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, JsonOptions), ct);
        logger.LogInformation("Exported full inventory ({Count} projects) to {Path}", projects.Count, path);
    }

    private static object BuildPayload(Models.DecomposedProject project) => new
    {
        project = new
        {
            id = project.Id,
            name = project.Name,
            source = project.Source,
            decomposedAt = project.DecomposedAt,
            commitHash = project.CommitHash
        },
        items = project.Items.Select(i => new
        {
            id = i.Id,
            category = i.Category.ToString(),
            name = i.Name,
            summary = i.Summary,
            details = JsonSerializer.Deserialize<object>(i.DetailsJson),
            rawMarkdown = i.RawMarkdown
        })
    };

    #endregion
}
