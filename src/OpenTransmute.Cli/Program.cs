using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTransmute;
using OpenTransmute.Cli;
using OpenTransmute.Cli.Commands;
using OpenTransmute.Data;

// DB lives next to the CWD — same default as the Blazor app so both can share data
string dbDir = Path.Combine(Directory.GetCurrentDirectory(), "DB");
Directory.CreateDirectory(dbDir);
string dbPath = Path.Combine(dbDir, "opentransmute.db");

// Build the host — JobRunner starts as a BackgroundService and waits for queued work
var host = Host.CreateDefaultBuilder()
    .ConfigureLogging(l =>
    {
        l.ClearProviders();
        l.AddConsole();
        l.SetMinimumLevel(LogLevel.Warning); // suppress hosting lifecycle noise
    })
    .ConfigureServices(services =>
    {
        services.AddOpenTransmuteCore($"Data Source={dbPath}");
        services.AddHttpClient();
    })
    .Build();

await host.Services.InitializeOpenTransmuteAsync();
await host.StartAsync();

// Load settings from the shared database (same UserSettings table the Blazor UI uses)
var settingsStore = new CliSettingsStore(
    host.Services.GetRequiredService<IDbContextFactory<AppDbContext>>());
CliSettings settings = settingsStore.Load();

// Build command tree
var root = new RootCommand("OpenTransmute CLI — decompose codebases and compose new systems with AI");
root.Subcommands.Add(DecomposeCommand.Build(host.Services, settings));
root.Subcommands.Add(ComposeCommand.Build(host.Services, settings));
root.Subcommands.Add(ImplementCommand.Build(host.Services, settings));
root.Subcommands.Add(VerifyCommand.Build(host.Services, settings));
root.Subcommands.Add(FixCommand.Build(host.Services));
root.Subcommands.Add(InventoryCommand.Build(host.Services));
root.Subcommands.Add(JobsCommand.Build(host.Services));
root.Subcommands.Add(SettingsCommand.Build(settings, settingsStore));

int exitCode = await root.Parse(args).InvokeAsync();

await host.StopAsync();
return exitCode;
