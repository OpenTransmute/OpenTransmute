---
title: Compose
parent: Workflows
nav_order: 2
---

# Compose
{: .no_toc }

<details open markdown="block">
  <summary>Contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Overview

Compose takes items from the composition inventory — algorithms, patterns, abstractions, and other portable ideas extracted from one or more decomposed projects — and synthesises a new architectural design from them. The AI validates the design for correctness, security, and performance, and produces hardened output in Markdown.

Unlike [Transmute](transmute), Compose lets you cherry-pick ideas from multiple projects and combine them however you like.

---

## The Inventory
{: #the-inventory }

The composition inventory is populated automatically when a decomposition reaches Phase 6. It is a flat catalogue of portable ideas extracted from codebases, organised into seven categories:

| Category | What it captures |
|---|---|
| **Algorithm** | Name, purpose, inputs/outputs, time/space complexity, pseudocode, test cases, security analysis |
| **Design Pattern** | Pattern name, where applied, problem solved, deviations from canonical form |
| **Invariant** | The condition, when it must hold, what breaks if violated |
| **Data Transformation** | Input shape, output shape, transformation logic, where it occurs |
| **Domain Vocabulary** | Term, definition as used in this system, why it matters |
| **Architectural Primitive** | The smallest indivisible building block; what it is, what is built on top of it |
| **Key Abstraction** | What it hides, what it exposes, what breaks if it leaks |

### Browsing the inventory

**Web app** — Open the **Inventory** screen to search, filter by category or project, expand items, and add them to the compose basket.

**CLI:**
```bash
otx inventory                              # list all items
otx inventory --category Algorithm         # filter by category
otx inventory --project my-project        # filter by project
otx inventory --search "retry"            # search by name/summary
otx inventory --search "retry" --verbose  # show full markdown
```

---

## Running a Compose Job

### Web app

1. On the **Inventory** screen, click **Add to basket** on the items you want.
2. Navigate to the **Compose** screen.
3. Review the basket — remove items or toggle grouping by category.
4. Expand **Prompt Preview** to see the exact prompt that will be sent to the AI.
5. Optionally fill in the target **Description**, **Environment**, and **Technology** fields.
6. Choose a backend and click **Run Compose**.

### CLI

```bash
# Compose from specific items (by name or GUID)
otx compose --output my-new-system \
  --items "Token Bucket Rate Limiter,Circuit Breaker Pattern" \
  --description "A resilient API gateway" \
  --technology "Go, gRPC" \
  --environment "Kubernetes"

# Include all items from a category
otx compose --output algo-showcase --category Algorithm

# Include all items from a project
otx compose --output new-system --project trading-engine

# Mix selection methods
otx compose --output hybrid \
  --project trading-engine \
  --category Algorithm \
  --items "some-extra-guid"
```

At least one of `--items`, `--category`, or `--project` is required.

See [CLI: compose](../cli/compose) for the full option reference.

---

## Guiding the Output

### Description, environment, and technology

These three fields shape what the AI designs:

- **Description** — what the target system should be and do
- **Environment** — where it runs (cloud, browser, embedded, mobile, etc.)
- **Technology** — the tech stack, language, or framework to target

```bash
otx compose --output payment-service \
  --project ecommerce-platform \
  --description "A payment processing microservice with idempotent retries" \
  --environment "AWS Lambda, managed by Terraform" \
  --technology "TypeScript, Node.js, DynamoDB"
```

### User ethos

The *user ethos* is a block of text describing your personal coding standards — naming conventions, error handling philosophy, testing requirements, security baselines, and anything else the AI should treat as a top-level authoritative instruction.

Set it once to apply it to every future Compose run:

```bash
otx settings --user-ethos "All code must be idiomatic Go. Errors are wrapped with fmt.Errorf \
  and %w. All exported functions have godoc comments. No global state."
```

Override it for a single run:

```bash
otx compose --output my-design --items "..." \
  --ethos "All public APIs must have OpenAPI annotations. Errors use RFC 7807."
```

The web app applies the ethos stored in **Settings** automatically.

---

## Output

Results are saved to `Output/Composition/<name>/compose-output.md` relative to the working directory.

From there, you can feed the output into the [Implement](implement) pipeline to generate working code.
