using MudBlazor;
using MudBlazor.Services;
using OpenTransmute;
using OpenTransmute.Services;

var builder = WebApplication.CreateBuilder(args);

// Blazor + MudBlazor
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(o => o.DetailedErrors = true)
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 1024 * 1024 * 10; // 10 MB
    });
builder.Services.AddMudServices();
builder.Services.AddMudMarkdownServices();
    

// UI state
builder.Services.AddSingleton<LlmSettingsService>();
builder.Services.AddSingleton<UserSettingsService>();

// All business logic (jobs, orchestration, data, LLM backends, inventory, …)
var dbDir  = Path.Combine(Directory.GetCurrentDirectory(), "DB");
Directory.CreateDirectory(dbDir);
var dbPath = Path.Combine(dbDir, "opentransmute.db");
builder.Services.AddOpenTransmuteCore($"Data Source={dbPath}");

builder.Services.AddHttpClient();

var app = builder.Build();

// Run EF migrations and restore job history
await app.Services.InitializeOpenTransmuteAsync();
// Restore persisted user settings into the in-memory singleton
await app.Services.GetRequiredService<UserSettingsService>().LoadAsync();

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
