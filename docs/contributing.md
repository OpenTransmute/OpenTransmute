---
title: Contributing
nav_order: 7
---

# Contributing & Extending
{: .no_toc }

<details open markdown="block">
  <summary>Contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Adding a New AI Backend

OpenTransmute has two backend interfaces depending on which pipeline you're extending.

### Decompose backend (`IDecomposeOrchestrator`)

Implement `IDecomposeOrchestrator` from `OpenTransmute.Orchestrator.Contracts`:

```csharp
public class MyDecomposeOrchestrator : IDecomposeOrchestrator
{
    public OrchestratorType Type => OrchestratorType.OpenAI; // reuse or extend the enum

    public IAsyncEnumerable<PhaseEvent> RunAsync(DecomposeRequest request, CancellationToken ct)
    {
        // yield PhaseStarted, LogLine, PhaseCompleted, PhaseFailed, etc.
    }
}
```

Register it in `ServiceRegistration.cs` inside `AddOpenTransmuteCore`:

```csharp
services.AddSingleton<IDecomposeOrchestrator, MyDecomposeOrchestrator>();
```

### Compose / Implement backend (`ILlmBackend`)

Implement `ILlmBackend` from `OpenTransmute.Core.Llm`:

```csharp
public class MyLlmBackend : ILlmBackend
{
    public Task<string> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        // call your provider, return the completed text
    }
}
```

Register it in `ServiceRegistration.cs`:

```csharp
services.AddSingleton<MyLlmBackend>();
```

Then add a selection option in `Components/Pages/Compose.razor` (web app) and/or the relevant CLI command handler.

### Error signalling

Throw these exceptions so the retry policy handles them correctly:

| Exception | When to throw |
|---|---|
| `LlmRateLimitException` | HTTP 429 or equivalent rate limit response |
| `LlmContextTooLargeException` | Context window exceeded |

The `RetryPolicy` in `OpenTransmute.Retry` handles both with backoff and context-reduction strategies.

---

## Adding a New Inventory Category

1. **Add the enum value** — open `src/OpenTransmute.Core/Models/InventoryCategory.cs` and add your new value.

2. **Create a section parser** — add `src/OpenTransmute.Core/Inventory/SectionParsers/MyNewSectionParser.cs` implementing `ISectionParser`:

   ```csharp
   public class MyNewSectionParser : ISectionParser
   {
       public InventoryCategory Category => InventoryCategory.MyNew;
       public InventoryItem? Parse(string heading, string body) { ... }
   }
   ```

3. **Register the parser** — add it to `Inventory/InventoryParser.cs` in the parser list.

4. **Add a migration** — run:

   ```bash
   dotnet ef migrations add AddMyNewCategory --project src/OpenTransmute.Core
   ```

5. **Update the Phase 6 prompt** — add a new numbered section to the Composition Inventory prompt in `decompose.md` following the pattern of the existing seven sections.

---

## Adding a New Decompose Phase

Phases are entirely data-driven. No code changes are required.

1. Open `src/OpenTransmute.Orchestrator/Prompts/decompose.md`.
2. Add a new `## Phase N — Title` section:

   ```markdown
   ## Phase N — My New Phase

   **Goal:** What this phase produces.
   **Model Weight:** thick | normal | thin
   **Prior Context:** 0,1,2  (or `none`)

   ` ` `
   Your prompt text here.
   Uses <project> and <path> substitution tokens.
   ` ` `
   ```

3. Rebuild: `dotnet build src/OpenTransmute.Orchestrator`

For expansion phases (discovery → per-item template), include:

```markdown
**Expansion Discovery:** json-array
**Expansion Template:** my-template-name
**Expansion Output Pattern:** codeMap/<project>/0N-{index:00}-{slug}.md
```

And add a second fenced code block for the per-item template prompt. See Phase 3 in `decompose.md` for a complete example.

---

## Project Structure

```
src/
├── OpenTransmute.Cli/              CLI (System.CommandLine) — otx
│   ├── Program.cs
│   ├── CliSettings.cs              Persisted CLI settings
│   └── Commands/                  One file per command
├── OpenTransmute.Core/             All business logic, shared by CLI + web
│   ├── Jobs/                      DecomposeJob, ComposeJob, ImplementJob, JobRunner
│   ├── Models/                    DecomposeOptions, ComposeOptions, ImplementOptions
│   ├── Phases/                    ComposeOrchestrator, ImplementOrchestrator
│   ├── Llm/                       ILlmBackend implementations
│   ├── Inventory/                 InventoryParser, InventoryExporter, SectionParsers
│   ├── Filtering/                 SourceFileFilter, .transmuteignore support
│   ├── Source/                    GitSourceFetcher, LocalSourceFetcher
│   ├── Retry/                     RetryPolicy for rate limits and context overflow
│   └── Data/                      AppDbContext, EF Core migrations
├── OpenTransmute.Orchestrator/     Decompose orchestration
│   ├── Contracts/                 IDecomposeOrchestrator, PhaseEvent, OrchestratorType
│   ├── Orchestration/             ClaudeOrchestrator, OpenAiOrchestrator
│   ├── Prompts/                   decompose.md, compose.md, transmute.md
│   ├── Parsing/                   PromptBuilder, substitution token resolution
│   └── Model/                     ModelProfile, ModelRegistry, PhaseSpec
└── OpenTransmute/                  Blazor Server web app
    ├── Program.cs
    ├── Components/Pages/           Decompose, Compose, Transmute, Implement, Inventory, etc.
    └── Services/                   LlmSettingsService, UserSettingsService
```

---

## Running Locally

```bash
# Web app
cd src/OpenTransmute
dotnet run

# CLI (without installing)
dotnet run --project src/OpenTransmute.Cli -- --help

# Tests
dotnet test
```

---

## Licence

MIT. Contributions welcome.
