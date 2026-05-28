using Microsoft.EntityFrameworkCore;
using OpenTransmute.Data;
using OpenTransmute.Models;

namespace OpenTransmute.Cli;

/// <summary>
/// Loads and saves <see cref="CliSettings"/> from the shared SQLite database
/// (<c>UserSettings</c> table). Both the CLI and Blazor UI read/write the same
/// singleton row, so settings configured in either surface are shared.
/// </summary>
public sealed class CliSettingsStore(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>
    /// Reads the <see cref="UserSettings"/> singleton row and maps it to <see cref="CliSettings"/>.
    /// Returns defaults if no row exists yet (first run).
    /// </summary>
    public CliSettings Load()
    {
        using AppDbContext db = dbFactory.CreateDbContext();
        UserSettings? stored = db.UserSettings.Find(UserSettings.SingletonId);
        if (stored is null) return new CliSettings();

        return new CliSettings
        {
            Orchestrator    = (OrchestratorType)stored.Orchestrator,
            ThickModel      = stored.ThickModel,
            RegularModel    = stored.RegularModel,
            ThinModel       = stored.ThinModel,
            OpenAiEndpoint  = stored.OpenAiEndpoint,
            MaxTurns        = stored.MaxTurns,
            MaxOutputTokens = stored.MaxOutputTokens,
            TimeoutMinutes  = stored.TimeoutMinutes,
            UserEthos       = stored.UserEthos
        };
    }

    /// <summary>
    /// Writes the current <see cref="CliSettings"/> values to the database,
    /// inserting the singleton row if it does not yet exist.
    /// </summary>
    public void Save(CliSettings settings)
    {
        using AppDbContext db = dbFactory.CreateDbContext();
        UserSettings? stored = db.UserSettings.Find(UserSettings.SingletonId);

        if (stored is null)
        {
            stored = new UserSettings { Id = UserSettings.SingletonId };
            db.UserSettings.Add(stored);
        }

        stored.Orchestrator    = (int)settings.Orchestrator;
        stored.OpenAiEndpoint  = settings.OpenAiEndpoint;
        stored.ThickModel      = settings.ThickModel;
        stored.RegularModel    = settings.RegularModel;
        stored.ThinModel       = settings.ThinModel;
        stored.MaxTurns        = settings.MaxTurns;
        stored.MaxOutputTokens = settings.MaxOutputTokens;
        stored.TimeoutMinutes  = settings.TimeoutMinutes;
        stored.UserEthos       = string.IsNullOrWhiteSpace(settings.UserEthos)
                                 ? null
                                 : settings.UserEthos.Trim();

        db.SaveChanges();
    }
}
