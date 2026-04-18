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

All pipelines (Decompose, Compose, Implement, Transmute) share a single backend abstraction: `ILlmExecutor` in `OpenTransmute.Llm`. The `JobOrchestrator` selects the right executor at runtime by matching `OrchestratorType`.

### Implement `ILlmExecutor`

```csharp
public class MyExecutor : ILlmExecutor
{
    public OrchestratorType BackendType => OrchestratorType.OpenAI; // reuse or extend the enum

    public async IAsyncEnumerable<LlmOutputEvent> ExecuteAsync(
        LlmExecutionContext ctx,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // yield LlmLine events as output arrives
        // yield LlmCompleted or LlmFailed as the final event
    }
}
```

Register it in `ServiceRegistration.cs` inside `AddOpenTransmuteCore`:

```csharp
services.AddSingleton<ILlmExecutor, MyExecutor>();
```

Then add a selection option in `Components/Pages/Decompose.razor` (web app) and/or the relevant CLI command handler.

### Error signalling

Deliver failures as a terminal `LlmFailed` event rather than throwing. The retry logic in `OpenTransmute.Retry` intercepts these and applies:

| Failure kind | Strategy |
|---|---|
| Rate limit (HTTP 429) | Exponential backoff (5 s → 20 s → 60 s → 120 s) |
| Context window exceeded | Reduces prior-context and retries once |

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

1. Open `src/OpenTransmute.Core/Prompts/decompose.md`.
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

3. Rebuild: `dotnet build src/OpenTransmute.Core`

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
│   ├── Data/                      AppDbContext, EF Core migrations
│   ├── Filtering/                 SourceFileFilter, .transmuteignore support
│   ├── Inventory/                 InventoryParser, InventoryExporter, SectionParsers
│   ├── Jobs/                      DecomposeJob, ComposeJob, ImplementJob, JobRunner
│   ├── Llm/                       ILlmExecutor, ClaudeSubprocessExecutor, OpenAiChatExecutor
│   ├── Models/                    OrchestratorType, DecomposeOptions, ComposeOptions, etc.
│   ├── Orchestration/             JobOrchestrator, PhaseEvent, RunContext
│   ├── Parsing/                   PromptBuilder, PromptTemplates
│   ├── Phases/                    Phase orchestration helpers
│   ├── Plugins/                   FileSystemPlugin (LLM agent file access)
│   ├── Prompts/                   decompose.md, compose.md, transmute.md (embedded resources)
│   ├── Retry/                     RetryPolicy for rate limits and context overflow
│   ├── Source/                    GitSourceFetcher, LocalSourceFetcher
│   ├── Writing/                   OutputWriter
│   └── ServiceRegistration.cs     DI registration for all services
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
