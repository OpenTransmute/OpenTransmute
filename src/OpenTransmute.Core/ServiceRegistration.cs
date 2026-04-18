using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTransmute.Data;
using OpenTransmute.Filtering;
using OpenTransmute.Inventory;
using OpenTransmute.Jobs;
using OpenTransmute.Llm;
using OpenTransmute.Models;
using OpenTransmute.Orchestration;
using OpenTransmute.Writing;
using OpenTransmute.Parsing;
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
        services.AddSingleton<ImplementJobPersistenceService>();
        services.AddHostedService<JobRunner>();

        // Source fetchers
        services.AddSingleton<ISourceFetcher, LocalSourceFetcher>();
        services.AddSingleton<ISourceFetcher, GitSourceFetcher>();

        // LLM executor implementations — all registered; JobOrchestrator selects by OrchestratorType
        services.AddSingleton<ILlmExecutor, ClaudeSubprocessExecutor>();
        services.AddSingleton<ILlmExecutor>(sp =>
            new OpenAiChatExecutor(
                sp.GetRequiredService<ILogger<OpenAiChatExecutor>>(),
                OrchestratorType.OpenAI));
        services.AddSingleton<ILlmExecutor>(sp =>
            new OpenAiChatExecutor(
                sp.GetRequiredService<ILogger<OpenAiChatExecutor>>(),
                OrchestratorType.Ollama));

        // Core orchestration
        services.AddSingleton<PromptTemplates>();
        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<OutputWriter>();
        services.AddSingleton<JobOrchestrator>();

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

        ImplementJobPersistenceService implementPersistence = services.GetRequiredService<ImplementJobPersistenceService>();
        foreach (ImplementJob job in await implementPersistence.LoadAllAsync())
            jobStore.Add(job);
    }
}
