using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTransmute;
using OpenTransmute.Cli;
using OpenTransmute.Cli.Commands;

// Load persisted settings before anything else
var settingsStore = new CliSettingsStore();
var settings      = settingsStore.Load();

// DB lives next to the CWD — same default as the Blazor app so both can share data
var dbDir  = Path.Combine(Directory.GetCurrentDirectory(), "DB");
Directory.CreateDirectory(dbDir);
var dbPath = Path.Combine(dbDir, "opentransmute.db");

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

// Build command tree
var root = new RootCommand("OpenTransmute CLI — decompose codebases and compose new systems with AI");
root.Subcommands.Add(DecomposeCommand.Build(host.Services, settings));
root.Subcommands.Add(ComposeCommand.Build(host.Services, settings));
root.Subcommands.Add(ImplementCommand.Build(host.Services, settings));
root.Subcommands.Add(InventoryCommand.Build(host.Services));
root.Subcommands.Add(JobsCommand.Build(host.Services));
root.Subcommands.Add(SettingsCommand.Build(settings, settingsStore));

int exitCode = await root.Parse(args).InvokeAsync();

await host.StopAsync();
return exitCode;
