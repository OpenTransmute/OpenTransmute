using Microsoft.EntityFrameworkCore;
using OpenTransmute.Data;
using OpenTransmute.Models;

namespace OpenTransmute.Services;

/// <summary>
/// Bridges the in-memory <see cref="LlmSettingsService"/> singleton with the database.
/// Call <see cref="LoadAsync"/> at startup to restore saved settings; call
/// <see cref="SaveAsync"/> when the user clicks Save on the Settings page.
/// </summary>
public sealed class UserSettingsService(
    IDbContextFactory<AppDbContext> dbFactory,
    LlmSettingsService llmSettings)
{
    /// <summary>
    /// Reads the persisted settings row and copies values into <see cref="LlmSettingsService"/>.
    /// No-ops if no row exists (first run).
    /// </summary>
    public async Task LoadAsync()
    {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync();
        UserSettings? stored = await db.UserSettings.FindAsync(UserSettings.SingletonId);
        if (stored is null) return;

        llmSettings.Orchestrator           = (OrchestratorType)stored.Orchestrator;
        llmSettings.OpenAiEndpoint         = stored.OpenAiEndpoint;
        llmSettings.ThickModel             = stored.ThickModel;
        llmSettings.RegularModel           = stored.RegularModel;
        llmSettings.ThinModel              = stored.ThinModel;
        llmSettings.MaxTurns               = stored.MaxTurns;
        llmSettings.MaxOutputTokens        = stored.MaxOutputTokens;
        llmSettings.TimeoutMinutes         = stored.TimeoutMinutes;
        llmSettings.ThickMaxOutputTokens   = stored.ThickMaxOutputTokens;
        llmSettings.RegularMaxOutputTokens = stored.RegularMaxOutputTokens;
        llmSettings.ThinMaxOutputTokens    = stored.ThinMaxOutputTokens;
        llmSettings.UserEthos              = stored.UserEthos;
    }

    /// <summary>
    /// Writes the current <see cref="LlmSettingsService"/> values to the database,
    /// inserting the singleton row if it does not yet exist. The API key is never saved.
    /// </summary>
    public async Task SaveAsync()
    {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync();
        UserSettings? stored = await db.UserSettings.FindAsync(UserSettings.SingletonId);

        if (stored is null)
        {
            stored = new UserSettings { Id = UserSettings.SingletonId };
            db.UserSettings.Add(stored);
        }

        stored.Orchestrator           = (int)llmSettings.Orchestrator;
        stored.OpenAiEndpoint         = llmSettings.OpenAiEndpoint;
        stored.ThickModel             = llmSettings.ThickModel;
        stored.RegularModel           = llmSettings.RegularModel;
        stored.ThinModel              = llmSettings.ThinModel;
        stored.MaxTurns               = llmSettings.MaxTurns;
        stored.MaxOutputTokens        = llmSettings.MaxOutputTokens;
        stored.TimeoutMinutes         = llmSettings.TimeoutMinutes;
        stored.ThickMaxOutputTokens   = llmSettings.ThickMaxOutputTokens;
        stored.RegularMaxOutputTokens = llmSettings.RegularMaxOutputTokens;
        stored.ThinMaxOutputTokens    = llmSettings.ThinMaxOutputTokens;
        stored.UserEthos              = string.IsNullOrWhiteSpace(llmSettings.UserEthos)
                                        ? null
                                        : llmSettings.UserEthos.Trim();

        await db.SaveChangesAsync();
    }
}
