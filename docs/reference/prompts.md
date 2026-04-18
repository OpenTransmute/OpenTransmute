---
title: Prompts
parent: Reference
nav_order: 5
---

# Prompts
{: .no_toc }

<details open markdown="block">
  <summary>Contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

OpenTransmute's prompt logic lives in Markdown files embedded as resources in the `OpenTransmute.Core` assembly. Customising them requires only editing a `.md` file and rebuilding.

---

## Prompt Files

| File | Used by | Purpose |
|---|---|---|
| `src/OpenTransmute.Core/Prompts/decompose.md` | Decompose pipeline | Defines all eight phases, their goals, model weights, prior context, and prompt text |
| `src/OpenTransmute.Core/Prompts/compose.md` | Compose pipeline | Composition and architectural synthesis prompt |
| `src/OpenTransmute.Core/Prompts/transmute.md` | Transmute and Implement pipelines | Standing guards and rules for all agentic code generation |

---

## Substitution Tokens

`PromptBuilder` in `OpenTransmute.Parsing` resolves these tokens at runtime before sending the prompt to the AI:

| Token | Resolved to |
|---|---|
| `<project>` | The project name |
| `<path>` | The local path to the source directory |
| `<ListItem.groupName>` | Expansion phase: the cluster name for this iteration |
| `<ListItem.role>` | Expansion phase: the cluster role description |
| `<ListItem.files>` | Expansion phase: the files belonging to this cluster |

---

## Phase Structure in `decompose.md`

Each phase follows this format:

~~~markdown
## Phase N — Title

**Goal:** Description of what the phase produces.
**Model Weight:** thick | normal | thin
**Prior Context:** comma-separated phase numbers, or `none`

```
The prompt text sent to the AI.
Uses <project> and <path> substitution tokens.
```
~~~

### Expansion phases

Phase 3 is an expansion phase — it runs a discovery step followed by one iteration per discovered cluster. Expansion phases include additional metadata:

~~~markdown
## Phase 3 — Component Specifications

**Goal:** ...
**Model Weight:** thick
**Prior Context:** none
**Expansion Discovery:** json-array
**Expansion Template:** cluster-spec
**Expansion Output Pattern:** codeMap/<project>/03-{index:00}-{slug}.md

**Step 1 — identify the components.**

```
Discovery prompt — must return a raw JSON array.
```

**Step 2 — spec each cluster.**

```
Per-cluster template — uses <ListItem.groupName>, <ListItem.role>, <ListItem.files>.
```
~~~

The `**Expansion Discovery:**` marker is what the orchestrator detects to identify a phase as an expansion phase.

---

## Adding a New Phase

1. Open `src/OpenTransmute.Core/Prompts/decompose.md`.
2. Add a new `## Phase N — Title` section following the conventions above.
3. Rebuild:

```bash
cd src/OpenTransmute.Core
dotnet build
```

No code changes are required. The new phase appears automatically in the web app's phase stepper and in `otx decompose`.

For expansion phases (where a discovery prompt returns a JSON list and a template is then run per item), add the `**Expansion Discovery:**` and `**Expansion Output Pattern:**` metadata and a second prompt block for the per-item template.

---

## Customising `compose.md`

The compose prompt drives both Compose and Transmute jobs. It receives the assembled inventory blocks (for Compose) or the full phase output (for Transmute), plus the target description, environment, and technology fields. Edit `compose.md` to change the synthesis logic, validation steps, or output format.

---

## The `transmute.md` Guards File

`transmute.md` contains standing rules that are prepended to every Transmute and Implement prompt — covering type safety, explicit visibility declarations, test assertion specificity, documentation placement, and EARS requirement verification. These rules apply to all agentic code generation and cannot be overridden by user ethos.
