using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTransmute.Data;
using OpenTransmute.Filtering;
using OpenTransmute.Inventory;
using OpenTransmute.Jobs;
using OpenTransmute.Llm;
using OpenTransmute.Orchestrator.Contracts;
using OpenTransmute.Orchestrator.Orchestration;
using OpenTransmute.Orchestrator.Output;
using OpenTransmute.Orchestrator.Parsing;
using OpenTransmute.Orchestrator.Retry;
using OpenTransmute.Phases;
using OpenTransmute.Source;

namespace OpenTransmute;

/// <summary>
/// Extension methods that register all OpenTransmute business-logic services into the DI container.
/// Used by both the web host and the CLI host to ensure identical service composition.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>
    /// Registers all OpenTransmute business-logic services. Call this from both the web host
    /// and the CLI host so they share identical behaviour.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="dbConnectionString">SQLite connection string, e.g. "Data Source=/path/opentransmute.db"</param>
    public static IServiceCollection AddOpenTransmuteCore(
        this IServiceCollection services,
        string dbConnectionString)
    {
        // Data
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(dbConnectionString));

        // Job infrastructure
        services.AddSingleton<JobStore>();
        services.AddSingleton<JobQueue>();
        services.AddSingleton<JobPersistenceService>();
        services.AddSingleton<ComposeJobPersistenceService>();
        services.AddHostedService<JobRunner>();

        // Source fetchers
        services.AddSingleton<ISourceFetcher, LocalSourceFetcher>();
        services.AddSingleton<ISourceFetcher, GitSourceFetcher>();

        // Decompose orchestration — shared services
        services.AddSingleton<PromptTemplates>();
        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<OutputWriter>();
        services.AddSingleton<OpenTransmute.Orchestrator.Retry.RetryPolicy>();

        // Decompose orchestrators — all registered; JobRunner selects by OrchestratorType
        services.AddSingleton<IDecomposeOrchestrator, ClaudeOrchestrator>();
        services.AddSingleton<IDecomposeOrchestrator>(sp =>
            new OpenAiOrchestrator(
                sp.GetRequiredService<PromptTemplates>(),
                sp.GetRequiredService<PromptBuilder>(),
                sp.GetRequiredService<OutputWriter>(),
                sp.GetRequiredService<OpenTransmute.Orchestrator.Retry.RetryPolicy>(),
                sp.GetRequiredService<ILogger<OpenAiOrchestrator>>(),
                OrchestratorType.OpenAI));
        services.AddSingleton<IDecomposeOrchestrator>(sp =>
            new OpenAiOrchestrator(
                sp.GetRequiredService<PromptTemplates>(),
                sp.GetRequiredService<PromptBuilder>(),
                sp.GetRequiredService<OutputWriter>(),
                sp.GetRequiredService<OpenTransmute.Orchestrator.Retry.RetryPolicy>(),
                sp.GetRequiredService<ILogger<OpenAiOrchestrator>>(),
                OrchestratorType.Ollama));

        // Compose / Transmute
        services.AddSingleton<ClaudeAgentBackend>();
        services.AddSingleton<OpenAiCompletionBackend>();
        services.AddSingleton<OpenTransmute.Retry.RetryPolicy>();
        services.AddSingleton<ComposeOrchestrator>();
        services.AddSingleton<ImplementOrchestrator>();

        // Supporting services
        services.AddSingleton<SourceFileFilter>();
        services.AddSingleton<InventoryParser>();
        services.AddSingleton<InventoryExporter>();

        return services;
    }

    /// <summary>
    /// Runs EF Core migrations and loads persisted job history. Call during app startup
    /// after the host is built.
    /// </summary>
    public static async Task InitializeOpenTransmuteAsync(this IServiceProvider services)
    {
        IDbContextFactory<AppDbContext> db = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await db.CreateDbContextAsync();
        await ctx.Database.MigrateAsync();

        JobPersistenceService persistence = services.GetRequiredService<JobPersistenceService>();
        JobStore jobStore = services.GetRequiredService<JobStore>();
        string outputRoot = Directory.GetCurrentDirectory();
        foreach (DecomposeJob job in persistence.LoadAll(outputRoot))
            jobStore.Add(job);

        ComposeJobPersistenceService composePersistence = services.GetRequiredService<ComposeJobPersistenceService>();
        foreach (ComposeJob job in await composePersistence.LoadAllAsync())
            jobStore.Add(job);
    }
}
