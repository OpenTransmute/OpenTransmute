---
title: Implement
parent: Workflows
nav_order: 4
---

# Implement
{: .no_toc }

<details open markdown="block">
  <summary>Contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Overview

Implement takes a compose or transmute output file and drives an AI agent to write a complete, working implementation from the spec. The agent reads the spec, plans the implementation, and writes code files directly to an output directory.

---

## Running an Implement Job

### Web app

After a Compose or Transmute job completes, the job detail page offers an **Implement** button that pre-fills the spec path and launches the Implement workflow.

You can also navigate to the **Projects** or **Job History** screen and start an Implement run from any completed compose job.

### CLI

```bash
# Implement from a compose output file
otx implement Output/Composition/my-design/compose-output.md \
  --project my-new-system \
  --output ./generated/my-new-system

# With explicit backend and model
otx implement path/to/spec.md \
  --orchestrator OpenAI \
  --api-key sk-... \
  --model gpt-4o \
  --max-turns 300 \
  --timeout 90
```

See [CLI: implement](../cli/implement) for the full option reference.

---

## Choosing a Backend

For the Implement step, **ClaudeCode is strongly recommended** when available. The `claude` CLI agent uses its own file-reading and file-writing tools to explore the output directory and write code directly — no file packing or intermediate steps required. It handles large, multi-file implementations naturally.

For OpenAI or Ollama backends, the agent is driven via the `FileSystemPlugin`, which provides read/write tools over the output directory.

---

## Max Turns

Implement runs are longer than Compose runs. The default `--max-turns` is **200**, which is appropriate for small-to-medium specifications. For complex specs generating many files, increase it:

```bash
otx implement spec.md --max-turns 400 --timeout 120
```

---

## Output

Generated code is written to the `--output` directory (default: `Output/Implementation/<project>/`). The agent creates all files and directories needed for the implementation.

The output directory is not cleared before a run. If you re-run Implement on the same directory, the agent will see its previous output and can build on or correct it.

---

## Tips

- **Provide a high-quality spec.** The quality of the implementation is directly proportional to the quality of the compose/transmute output. If the spec is vague, the implementation will be too. Consider adding a [user ethos](compose#user-ethos) to set concrete standards.
- **Use the thick model.** The Implement step defaults to the thick model (the most capable one). Don't downgrade unless you have a cost reason — complex multi-file implementations benefit significantly from the largest context window and strongest reasoning.
- **Check the output directory first.** Implement does not clean the output directory. If you're re-running after a partial attempt, review what's already there so you understand what the agent will see.
