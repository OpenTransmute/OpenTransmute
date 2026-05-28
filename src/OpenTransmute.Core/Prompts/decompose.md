# Code Mapping Prompts

A reusable prompt system for fully documenting any codebase so it can be
re-implemented from scratch in any language or platform.

The output is a set of Markdown files that together constitute a **language-agnostic
specification** — not a tutorial, not a summary, but a complete behavioral contract.

---

## Output Rules

These rules apply to every phase. They are not suggestions.

- **Simple phases (0, 1, 2, 4, 5, 6, 7):** Your entire response MUST be valid Markdown.
  Output ONLY the document content — no preamble, no sign-off, no explanation of what
  you are about to do. Start with the first heading. End with the last sentence.
  Do not wrap the output in a code fence.

- **Expansion discovery (Phase 3, Step 1):** Your entire response MUST be a single
  valid JSON array. No prose before or after it. No markdown. No code fence. Raw JSON only.

- **Expansion specs (Phase 3, Step 2):** Your entire response MUST be valid Markdown.
  Same rules as simple phases above.

- **SHELL TOOLS ARE BLOCKED.** Do NOT use shell, powershell, bash, or any command-line
  execution tools. They will be rejected and you will waste a turn. Use only file-reading
  tools (view, read, glob, grep). If a glob result is too large, use a narrower pattern
  (e.g. `src/**/*.java` instead of `**/*`) or use grep to find specific content.

The orchestrator captures your stdout exactly as-is and writes it to disk.
Any text outside the required format corrupts the output file.

---

## How to Use

Run these prompts **in sequence** against the project.  Each phase builds on the
previous one.  Output files are saved to sequentially-numbered files in `codeMap/<project>/`:

```
00-index.md
01-structural-survey.md
02-initialization-flow.md
03-01-<first-component>.md
03-02-<second-component>.md
...
04-data-formats.md
05-reimplementation-checklist.md
06-composition-inventory.md
07-ethos.md
```

The number of component files (Phase 3) varies by project.  Start Phase 3 only
after Phase 2 is complete.  Phases 4 and 5 always land at `04-` and `05-`
regardless of how many component files Phase 3 produces.

Try to honor the Model Weight.  Thick is for heavy thinking workloads.
Regular is for less demanding workloads.  Thin is for light workloads.

---

## Phase 0 — Index & Architecture Overview

**Goal:** Produce a navigational index and a concise architectural summary that
orients a reader before they open any other document.
**Model Weight:** regular
**Prior Context:** none

```
You are mapping a project for re-implementation.

Project: <project>

Write a short index document containing:

1. One paragraph describing what the project is and what problem it solves.

2. A table listing every other map document by phase and purpose.
   Do NOT predict filenames — list by phase description only:
   Phase 1 Structural Survey, Phase 2 Initialization & Runtime Flow,
   Phase 3 Component Specifications (one document per component cluster),
   Phase 4 Data Formats & Protocols, Phase 5 Re-implementation Checklist,
   Phase 6 Composition Inventory, Phase 7 Ethos & Style Fingerprint.
   For each, write a one-line description of what a reader will find there.

3. "Architecture in one paragraph" — describe how the major subsystems fit
   together at runtime, without enumerating every detail.  A reader should be
   able to hold the whole system in their head after reading this paragraph.

4. "Key design decisions" — list 4–8 non-obvious choices the original authors
   made that a re-implementer must understand before writing any code.  For
   each: state the decision, and explain what breaks or becomes hard if the
   re-implementer ignores it.

REQUIRED SECTIONS (every section below must appear as a heading in your output):
  1. Project description
  2. Document index table
  3. Architecture in one paragraph
  4. Key design decisions

OUTPUT RULE: Respond with valid Markdown only. No preamble. No sign-off.
Start with the first heading. End with the last sentence.
```

---

## Phase 1 — Structural Survey

**Goal:** Understand the file layout, entry points, and high-level purpose of every
directory before reading any code.
**Model Weight:** regular
**Prior Context:** none

```
You are mapping a project for re-implementation.

Project root: <path>

1. List the full directory tree, at least 2 levels deep.  For each top-level
   directory, write one sentence describing its role.

2. Identify the primary entry point(s) — the file(s) a user or the OS invokes
   first.

3. Identify all configuration files (dotfiles, JSON, YAML, TOML, env files).
   For each, list every key/variable it exposes, its type, default value, and
   one-line description.

4. List every external dependency (packages, system binaries, optional tools).
   For each: name, version constraint if any, whether it is required or optional,
   and what breaks if it is absent.

5. Identify the build/test/install toolchain (Makefile, package.json scripts,
   CMakeLists, etc.) and list every defined task with its command and purpose.

Use tables where lists would be repetitive.  Do not summarize — be exhaustive.

REQUIRED SECTIONS (every section below must appear as a heading in your output):
  1. Directory tree
  2. Entry points
  3. Configuration files
  4. External dependencies
  5. Build/test/install toolchain

OUTPUT RULE: Respond with valid Markdown only. No preamble. No sign-off.
Start with the first heading. End with the last sentence.
```

---

## Phase 2 — Initialization & Runtime Flow

**Goal:** Trace the exact sequence of events from cold start to ready state, and
identify all extension/hook points.
**Model Weight:** regular
**Prior Context:** 1

```
You are mapping a project for re-implementation.

Using the structural survey in 01-structural-survey.md, read the entry point
file(s) and all files they import/source/require, following the call chain.

Document the following as a numbered sequence:

1. Every step in the startup sequence in order, including:
   - Which file/function runs
   - What state it reads (env vars, config files, previous steps)
   - What state it produces (variables set, files written, hooks registered)
   - Whether it can be skipped or overridden, and how

2. All hook/event/callback registration points:
   - Name of the hook
   - When it fires (before/after what)
   - What arguments it receives
   - What a subscriber is allowed to do
   - How to register/deregister

3. The shutdown/cleanup sequence (if any), same detail as startup.

4. Any background/async tasks: what they do, how they communicate results back,
   how errors are surfaced.

Use a flow diagram in Mermaid syntax if it helps clarify ordering.  Then write
prose that fully explains each step — the diagram is a supplement, not a
replacement.

REQUIRED SECTIONS (every section below must appear as a heading in your output):
  1. Startup sequence
  2. Hook/event/callback registration points
  3. Shutdown/cleanup sequence
  4. Background/async tasks

OUTPUT RULE: Respond with valid Markdown only. No preamble. No sign-off.
Start with the first heading. End with the last sentence.
```

---

## Phase 3 — Component Specifications

**Goal:** Document each major subsystem in enough detail that it can be written
from scratch with no reference to the original code.
**Model Weight:** thick
**Prior Context:** none
**Expansion Discovery:** json-array
**Expansion Template:** cluster-spec
**Expansion Output Pattern:** codeMap/<project>/03-{index:00}-{slug}.md

**Step 1 — identify the components.**  Run this prompt first:

```
List every logically distinct subsystem in this project.  For each, name it,
name the files that implement it, and write one sentence on its role.

Then assemble the subsystems into thematic groups (e.g. "plugin system",
"theme system", "core library utilities", "CLI interface").  Each group
will become one spec document.  Aim for 3–8 groups — too few means files
become unwieldy; too many means the reader loses the thread.

OUTPUT RULE: Respond with a raw JSON array only. No prose. No markdown. No code fence.
Your entire response must be parseable by JSON.parse().

The array must have this exact shape:
[
  {
    "groupName": "example",
    "role": "short description of the subsystem group",
    "files": ["path/to/file1", "path/to/file2"]
  }
]
```

**Step 2 — spec each cluster.**  Run this prompt once per cluster,
replacing `<ListItem.groupName>`, `<ListItem.role>`, and `<ListItem.files>` each time:

```
You are mapping a project for re-implementation.

Group: <ListItem.groupName>
Role: <ListItem.role>
Files: <ListItem.files>

Write a complete specification for every component in this group.  Assume
the reader will implement them from scratch in a different language.  For
each component include:

1. **Purpose** — What problem does it solve?  What would break if it were
   missing?

2. **Public interface** — Every function/method/command/event exposed to
   other components or end users.  For each:
   - Signature (name, inputs with types, outputs with types)
   - Preconditions the caller must satisfy
   - Postconditions guaranteed on return
   - Side effects (files written, state mutated, hooks fired)
   - Error conditions and how they are signaled

3. **Internal state** — All variables/data structures maintained across
   calls.  For each: name, type, initial value, what mutations are valid.

4. **Algorithms** — Any non-trivial logic (caching, invalidation, search,
   parsing).  Describe in plain English with enough precision that a
   competent programmer can implement it without guessing.

5. **Integration points** — What other components does this call?  What
   calls this component?

6. **Configuration** — Every user-facing option that changes behavior:
   name, type, default, effect.

7. **Extension interface** (if applicable) — If this component is itself
   an extension point (plugins, themes, drivers, etc.), document the full
   contract: discovery, lifecycle, what an extension must/may/must-not do,
   host APIs available to extensions, and an annotated minimal template.

8. **Edge cases and known quirks** — Behaviors that are surprising,
   platform-specific, or deliberately different from a naive implementation.

9. **Composition extracts** — Pull out what is portable and reusable:
   - Notable algorithms: name, purpose, time/space complexity, pseudocode, and 3–5 EARS
     requirements stating its behavioral contract (inputs, outputs, edge cases)
   - Design patterns: name, where applied, why it was chosen
   - Core invariants: conditions that must hold at all times for this component, expressed
     as EARS requirements
   - Key abstractions: what they hide, what they expose, what breaks if they leak
   - Domain vocabulary: terms used with a specific meaning a re-implementer must know

Be exhaustive.  Do not omit a function because it seems simple.

REQUIRED SECTIONS (every section below must appear for each component):
  1. Purpose
  2. Public interface
  3. Internal state
  4. Algorithms
  5. Integration points
  6. Configuration
  7. Extension interface (if applicable — state "N/A" if not)
  8. Edge cases and known quirks
  9. Composition extracts

OUTPUT RULE: Respond with valid Markdown only. No preamble. No sign-off.
Start with the first heading. End with the last sentence.
```

---

## Phase 4 — Data Formats & Protocols

**Goal:** Document every file format, wire protocol, inter-process message, or
structured data type the project reads or writes.
**Model Weight:** thick
**Prior Context:** none

```
You are mapping a project for re-implementation.

1. Identify every file format the project reads or writes (config files, cache
   files, lock files, generated code, etc.).  For each:
   - Full grammar or schema (BNF, JSON Schema, or equivalent)
   - Example of a minimal valid instance
   - Example of a maximal/complex valid instance
   - Versioning scheme, if any
   - Backward-compatibility guarantees

2. Identify every inter-process or inter-component protocol (pipes, sockets,
   signals, shared memory, DBus, etc.).  For each:
   - Message types and their binary/text encoding
   - Sequence diagram for a typical exchange
   - Error handling and retry behavior

3. Identify every public API surface (CLI flags, environment variables, library
   exports, REST endpoints, etc.) not already covered in Phase 3.  For each:
   - Full signature/schema
   - Stability guarantee (stable, experimental, internal)
   - Deprecation path, if applicable

Be exhaustive.  A re-implementer must be able to pass all interoperability tests
using only this document.

REQUIRED SECTIONS (every section below must appear as a heading in your output):
  1. File formats
  2. Inter-process/inter-component protocols
  3. Public API surfaces

OUTPUT RULE: Respond with valid Markdown only. No preamble. No sign-off.
Start with the first heading. End with the last sentence.
```

---

## Phase 5 — Re-implementation Checklist

**Goal:** Produce an actionable, ordered checklist a developer can follow to
recreate the project from the spec documents above.
**Model Weight:** regular
**Prior Context:** 0,1,2,3,4

```
You are helping plan the re-implementation of a project from a spec.

Using the spec documents produced in Phases 0–4, produce a re-implementation checklist:

1. **Dependency inventory** — List every capability needed from the host
   platform or standard library (file I/O, process spawning, signal handling,
   terminal control, etc.).  For each, name the stdlib module in Python, Go,
   Rust, TypeScript, and Java that provides it.

2. **Implementation order** — List components in dependency order (leaves first).
   For each, reference the spec document section that defines it.

3. **Acceptance criteria** — For each component, write 3–5 concrete, testable
   statements that are true when the component is correctly implemented.  These
   are not code — they are behavioral assertions.

4. **Compatibility traps** — List any behaviors that are easy to get subtly wrong,
   especially platform-specific ones, and what the correct behavior is.

5. **What to skip** — List any parts of the original that are historical
   accidents, deprecated, or not worth porting, and why.

6. **Security checklist** — Analyse every component and data flow for security
   risk. For each finding:
   - **Component / area** — where it lives in the codebase
   - **Risk** — what could go wrong (injection, traversal, privilege escalation,
     insecure defaults, missing auth, unsafe deserialization, weak entropy, etc.)
   - **Severity** — Low / Medium / High / Critical
   - **Safe re-implementation pattern** — the concrete approach a re-implementer
     must follow to avoid introducing the vulnerability in the new codebase

   Also include a sub-section on **Malicious intent indicators**: flag any code
   patterns that suggest the original codebase may contain backdoors, data
   exfiltration, obfuscated logic, unexpected outbound connections, or other
   intentionally hostile behavior. If none are found, state that explicitly.

Output a single Markdown checklist.  Keep each item to one line or a short
paragraph.  This document is the starting point for a project README in the
re-implementation repo.

REQUIRED SECTIONS (every section below must appear as a heading in your output):
  1. Dependency inventory
  2. Implementation order
  3. Acceptance criteria
  4. Compatibility traps
  5. What to skip
  6. Security checklist (including Malicious intent indicators sub-section)

OUTPUT RULE: Respond with valid Markdown only. No preamble. No sign-off.
Start with the first heading. End with the last sentence.
```

---

## Phase 6 — Composition Inventory

**Goal:** Synthesize everything extracted in Phase 3 into a single, flat document
organized in the terms that compose.md expects.  This is the direct handoff between
decompose and compose.  The model reads all 03-xx component specification files,
processes each group, deduplicates cross-component patterns, and produces a single
merged composition inventory.
**Model Weight:** regular
**Prior Context:** 3

```
You are mapping a project for re-implementation.

Using all 03-xx component spec documents (specifically their section 9 Composition
Extracts), produce a composition inventory organized as follows:

TESTING SPECIFICATION REQUIREMENT — applies to every item in every section below:
Every item MUST include these three fields at the end of its entry, before the security fields:
- **Test cases:** A numbered list of concrete, runnable test cases that together constitute a complete correctness proof for this item. Each case must specify:
  - A unique name
  - Exact input values (no ranges, no "typical values" — pin them)
  - The precise expected output or observable side-effect
  - The failure mode to distinguish from a near-miss (what a wrong implementation would return instead)
  Do not write test cases that pass trivially or can be satisfied by a stub. Every case must fail against an incorrect implementation.
- **Boundary and adversarial cases:** A numbered list of edge inputs the implementation must handle correctly: empty inputs, maximum sizes, type boundaries, malformed data, concurrent calls, and any domain-specific extremes. For each: the input, the required behavior, and why a naive implementation would get it wrong.
- **Invariant assertions:** One or more executable post-conditions that must hold after every call, expressed as boolean predicates. These are the assertions a test harness should inject as permanent guards. If the item has no meaningful invariants, state that explicitly — do not omit the field.
- **Anti-cheat probes:** Explicit measures to detect an implementation that memorises or special-cases the fixed test inputs rather than executing the real logic. These MUST include:
  - At least two property-based / generative cases: a rule expressed over a parameterised input family (e.g. "for all N in [1, 1000], f(N) == expected-formula(N)") so that no finite lookup table can pass.
  - At least one cross-verification case: derive the expected answer via a second independent method or known-good reference, then assert both paths agree. The two methods must differ structurally so that a hardcoded switch cannot satisfy both.
  - At least one mutation-detection case: identify a minimal single-line change to the algorithm that would produce a wrong answer, specify the mutant, and provide the input/output pair that catches it. If the test suite would pass the mutant, the tests are insufficient.
  - A structural-execution probe where applicable: an assertion that a key internal operation actually ran (e.g. a counter, a log entry, a side-effect) rather than merely that the return value looks correct.

Be brutal. A test suite built from these fields must be capable of catching a plausible but incorrect reimplementation, including one that hardcodes known answers. If the item manipulates data, processes input, enforces a contract, or integrates with other components, there is no excuse for weak tests. Vague cases ("it should return the right value") are forbidden.

SECURITY ANALYSIS REQUIREMENT — applies to every item in every section below:
Every item MUST include these two fields at the end of its entry:
- **Security score:** [1–10] where 1 = no security concern, 10 = critical risk.
  Score based on: potential for injection attacks, privilege escalation, data
  exposure, path traversal, denial of service, malicious misuse, or unsafe
  implementation patterns that a naive re-implementer would likely introduce.
- **Security notes:** [Concrete description of the concern and the safe pattern,
  OR "No significant concerns identified." if the score is 1 or 2.]

Be honest and thorough — if an algorithm touches user input, file paths, network
data, cryptography, authentication, or authorisation it almost certainly has a
non-trivial score.  Do not give every item a 1.

1. **Algorithms** — Every notable algorithm identified across all components.
   For each: name, purpose, inputs/outputs, time and space complexity, pseudocode
   or a description precise enough to reimplement without seeing the original source,
   and 3–5 EARS requirements stating its behavioral contract.
   Then add the three mandatory testing fields, then the two mandatory security fields.

2. **Design Patterns** — Every identifiable design pattern applied in the codebase.
   For each: pattern name, where it appears, what problem it solves here, and any
   deviations from the canonical form.
   Then add the three mandatory testing fields, then the two mandatory security fields.

3. **Invariants** — All invariants, especially those that cross component boundaries.
   For each: the condition, when it must hold, what breaks if it is violated, and
   an EARS requirement for each invariant condition.
   Then add the three mandatory testing fields, then the two mandatory security fields.

4. **Data Transformations** — Every significant transformation of data.
   For each: input shape, output shape, transformation logic, where it occurs, and
   EARS requirements covering the normal path and any rejection/error conditions.
   Then add the three mandatory testing fields, then the two mandatory security fields.

5. **Domain Vocabulary** — Terms the codebase uses with a specific meaning.
   For each: term, definition as used in this system, and why it matters for
   re-implementation.
   Then add the three mandatory testing fields, then the two mandatory security fields.

6. **Architectural Primitives** — The smallest indivisible building blocks of the
   design: the types, interfaces, or concepts everything else is built on top of.
   Then add the three mandatory testing fields, then the two mandatory security fields.

7. **Key Abstractions** — The major abstractions that hide complexity.
   For each: what it hides, what it exposes, and what breaks if it leaks.
   Then add the three mandatory testing fields, then the two mandatory security fields.

8. **Ethos Fingerprint** — The unwritten rules of the codebase: how it looks,
   what it values, and how it behaves under pressure.  Extract and enumerate
   the following sub-categories.  For each entry, if the practice diverges
   from widely-accepted good practice (e.g. SOLID, clean code, language idioms,
   community style guides), append a **Divergence note:** explaining what the
   conventional approach would be and whether the deviation appears deliberate.
   Do not omit sub-categories; if a sub-category has no examples, state that
   explicitly rather than skipping it.

   - **Naming conventions** — identifier casing and format (types, variables,
     constants, files, packages, tests); abbreviation tolerance; acronym
     treatment; naming patterns for related families (e.g. FooReader/FooWriter).
   - **Error handling philosophy** — fail-fast vs. defensive; panic vs. return
     error; exception hierarchies; sentinel values; error message tone and
     format; how errors are wrapped, logged, or surfaced to callers.
   - **Abstraction discipline** — preferred layer thickness; where the codebase
     reaches through abstractions; when logic is inlined vs. delegated; tolerance
     for leaky abstractions.
   - **Logging and observability** — what events are logged and at which level;
     log message format and verbosity norms; use of structured vs. free-text
     logging; tracing/metrics idioms if present.
   - **Concurrency and async patterns** — threading model; async/await vs.
     callbacks vs. futures; how shared state is protected; where blocking calls
     appear and whether that is intentional.
   - **Configuration and magic values** — how constants are declared; whether
     magic literals appear inline; config layering and override philosophy.
   - **Dependency and coupling style** — constructor injection vs. service
     locator vs. globals; how inter-module dependencies are expressed; tolerance
     for circular references.
   - **Comment and documentation style** — when comments appear; what they say
     (intent vs. mechanics); doc-comment coverage and format; presence or absence
     of TODO/FIXME conventions.
   - **Code organisation preferences** — file length norms; one-type-per-file vs.
     grouping; folder-by-layer vs. folder-by-feature; test co-location.
   - **Test philosophy** — unit vs. integration preference; assertion style and
     verbosity; mock tolerance; test naming conventions; coverage expectations.

Output as a flat, scannable document.  No prose padding.  Every entry must be
concrete and self-contained — a reader with no access to the source code must be
able to use this document as a complete reference.

REQUIRED SECTIONS (every section below must appear as a heading in your output):
  1. Algorithms
  2. Design Patterns
  3. Invariants
  4. Data Transformations
  5. Domain Vocabulary
  6. Architectural Primitives
  7. Key Abstractions
  8. Ethos Fingerprint (with all listed sub-categories)

OUTPUT RULE: Respond with valid Markdown only. No preamble. No sign-off.
Start with the first heading. End with the last sentence.
```

---

## Phase 7 — Ethos & Style Fingerprint

**Goal:** Produce a standalone style guide derived entirely from reading the real
codebase, not from assumptions.  This document is the direct input compose uses
to make re-implemented code feel indistinguishable from the original in structure,
naming, and character — while being flagged where the original deviates from good
practice so compose can make an informed choice.
**Model Weight:** regular
**Prior Context:** 1,2,3

```
You are mapping a project for re-implementation.

Using the structural survey (01), initialization flow (02), and all component
specs (03-xx), extract the implicit style rules and values that govern how the
codebase was written.  Your goal is not to describe what the code does but how
it was written and why it feels the way it feels.

For each section below, be concrete and evidence-based.  Every claim must be
supported by a real example drawn from the codebase: quote the identifier,
snippet, or pattern.  Generalisations without examples are not acceptable.

If a practice diverges from widely-accepted good practice (language idioms,
community style guides, SOLID principles, OWASP, etc.), append a
**Divergence note:** that states what the conventional approach would be,
assesses whether the deviation appears deliberate or accidental, and advises
whether compose should replicate or correct it.

---

### 1. Naming Conventions

For each of the following identifier categories, state the observed casing
scheme, any prefix/suffix patterns, abbreviation tolerance, and acronym
treatment.  Provide at least two examples per category.

- Types / classes / interfaces
- Functions / methods
- Variables and parameters
- Constants and enum members
- Files and directories
- Test files and test cases
- Any domain-specific naming families (e.g. paired Reader/Writer, *Manager, *Service)

---

### 2. Error Handling Philosophy

- Is the primary style fail-fast, defensive, or mixed?
- How are errors propagated to callers (return codes, exceptions, Result types, panics)?
- What is the error message tone and format (user-facing vs. developer-facing, capitalisation, punctuation)?
- How are errors wrapped or annotated as they move up the call stack?
- Are errors logged at the site of occurrence, at the boundary, or both?
- What happens when an unrecoverable error is encountered?

---

### 3. Abstraction Discipline

- What is the typical layer thickness — thin pass-throughs or fat coordinating objects?
- Where does the codebase reach through an abstraction to access an underlying detail?
- At what point does the codebase prefer inlining over delegation?
- Are there leaky abstractions?  Name them and describe what leaks.

---

### 4. Logging and Observability

- What events are consistently logged?
- What log levels are in use, and how are they applied?
- Is logging structured (key-value / JSON) or free-text?
- What is the verbosity norm for happy-path vs. error-path?
- Are there tracing, metrics, or telemetry idioms?

---

### 5. Concurrency and Async Patterns

- What concurrency model is in use (threads, async/await, actors, event loop, etc.)?
- How is shared mutable state protected?
- Are there identifiable places where blocking calls appear in async contexts?
  If so, is this deliberate?
- How are async errors surfaced?

---

### 6. Configuration and Magic Values

- How are constants declared (named constants, enums, config files, inline literals)?
- Are magic numbers or strings inlined?  Where?
- What is the config layering model (defaults → file → env → flags)?
- How are sensitive configuration values handled?

---

### 7. Dependency and Coupling Style

- Is dependency injection used?  What flavour (constructor, property, service locator, ambient)?
- How are inter-module dependencies expressed and enforced?
- Are there global or ambient singletons?  Where?
- Is there tolerance for circular references?

---

### 8. Comment and Documentation Style

- When do comments appear (always, sparingly, only for non-obvious logic)?
- Do comments describe intent, mechanics, or both?
- What doc-comment format is used (JSDoc, XML, Godoc, etc.)?
- Are there TODO/FIXME conventions and are they tracked?
- What is the ratio of commented to uncommented exported symbols?

---

### 9. Code Organisation Preferences

- What is the typical file length?
- Is the convention one type per file, or are types grouped?
- Is the folder structure organised by layer (controllers/, models/) or by
  feature (auth/, billing/)?
- Are test files co-located with source files or in a separate tree?

---

### 10. Test Philosophy

- What is the balance between unit tests, integration tests, and end-to-end tests?
- What assertion library/style is used, and how verbose are assertions?
- What is the tolerance for mocks and fakes vs. real dependencies?
- How are test cases named?
- Is there an observable coverage target or enforcement?

---

### 11. Overall Character

Write two to four paragraphs that synthesise the above into a qualitative
description of the codebase's "feel."  A developer who reads this section
should be able to recognise the style in a new file without having been told
which project it belongs to.  Be honest: note where the codebase is
inconsistent, where it shows signs of multiple authors with different habits,
and where it appears to have evolved away from an earlier style.

---

CODE EXAMPLES MUST BE SHORT: 2 to 10 lines maximum, showing the pattern or
identifier.  Do NOT copy entire files, methods, or large blocks of source code.
If more context is needed, describe it in prose.

REQUIRED SECTIONS (every section below must appear as a heading in your output):
  1. Naming Conventions
  2. Error Handling Philosophy
  3. Abstraction Discipline
  4. Logging and Observability
  5. Concurrency and Async Patterns
  6. Configuration and Magic Values
  7. Dependency and Coupling Style
  8. Comment and Documentation Style
  9. Code Organisation Preferences
  10. Test Philosophy
  11. Overall Character

OUTPUT RULE: Respond with valid Markdown only. No preamble. No sign-off.
Start with the first heading. End with the last sentence.
```

---

## Tips for Good Maps

- **Read code, not just comments.** Comments lie; code is ground truth.
- **Trace the unhappy path.** Error handling reveals design intent.
- **Note what is NOT configurable.** Hard-coded behavior is a porting decision.
- **Flag platform-specific code explicitly.** These become porting tasks.
- **Record version at time of mapping.** Specs drift; pin the commit hash.
- **One function = one truth.** If two documents disagree, re-read the source.
