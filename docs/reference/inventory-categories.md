---
title: Inventory Categories
parent: Reference
nav_order: 2
---

# Inventory Categories

The composition inventory organises extracted knowledge into seven categories. Every item across all categories includes test cases, boundary cases, invariant assertions, anti-cheat probes, and a security analysis.

---

## Algorithm

Captures a named, reusable computational procedure.

| Field | Contents |
|---|---|
| Name | Identifier used in the codebase |
| Purpose | What problem it solves; what breaks if absent |
| Inputs / Outputs | Types and shapes |
| Time complexity | Big-O notation |
| Space complexity | Big-O notation |
| Pseudocode | Precise enough to reimplement without seeing the original |
| EARS requirements | 3–5 behavioral requirements (normal path + error conditions) |
| Test cases | Concrete inputs, exact expected outputs, failure mode descriptions |
| Boundary and adversarial cases | Empty inputs, maximum sizes, malformed data, concurrent calls |
| Invariant assertions | Executable post-conditions as boolean predicates |
| Anti-cheat probes | Property-based cases, cross-verification, mutation-detection |
| Security score | 1–10 |
| Security notes | Concrete concern and safe pattern, or "No significant concerns" |

---

## Design Pattern

Captures an identifiable design pattern as applied in this codebase.

| Field | Contents |
|---|---|
| Pattern name | Canonical pattern name (GoF, POSA, etc.) |
| Where applied | Files, classes, or subsystems where the pattern appears |
| Problem solved | The specific problem this application addresses |
| Deviations | How this application differs from the canonical form, and why |
| + Testing fields | (same as Algorithm) |
| + Security fields | (same as Algorithm) |

---

## Invariant

Captures a condition that must hold at all times.

| Field | Contents |
|---|---|
| The condition | Precise statement of the invariant |
| When it must hold | Pre-call, post-call, always, during specific operations |
| What breaks if violated | Concrete failure modes |
| EARS requirements | One or more requirements expressing the invariant condition |
| + Testing fields | (same as Algorithm) |
| + Security fields | (same as Algorithm) |

---

## Data Transformation

Captures a significant transformation of data.

| Field | Contents |
|---|---|
| Input shape | Type, structure, constraints |
| Output shape | Type, structure, constraints |
| Transformation logic | Step-by-step description |
| Where it occurs | Files or components |
| EARS requirements | Normal path + rejection/error conditions |
| + Testing fields | (same as Algorithm) |
| + Security fields | (same as Algorithm) |

---

## Domain Vocabulary

Captures a term the codebase uses with a specific meaning.

| Field | Contents |
|---|---|
| Term | The identifier or phrase |
| Definition | How this system uses it (not the dictionary definition) |
| Why it matters | What a re-implementer gets wrong if they misunderstand this term |
| + Testing fields | (same as Algorithm) |
| + Security fields | (same as Algorithm) |

---

## Architectural Primitive

Captures the smallest indivisible building block of the design — the types, interfaces, or concepts everything else is built on.

| Field | Contents |
|---|---|
| Name | The primitive's identifier |
| What it is | Precise definition |
| What is built on top of it | Components or abstractions that depend on this primitive |
| + Testing fields | (same as Algorithm) |
| + Security fields | (same as Algorithm) |

---

## Key Abstraction

Captures a major abstraction that hides complexity.

| Field | Contents |
|---|---|
| Name | The abstraction's identifier |
| What it hides | The complexity or implementation detail concealed |
| What it exposes | The interface available to consumers |
| What breaks if it leaks | Concrete failure modes when the abstraction is violated |
| + Testing fields | (same as Algorithm) |
| + Security fields | (same as Algorithm) |
