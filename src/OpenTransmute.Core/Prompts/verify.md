# Decomposition Verification Prompt

Audits decomposition documents against actual source code, producing
structured JSON so the pipeline can aggregate counts, fixes, and
scorecards deterministically — no LLM counting.

This prompt is run once per decomposition output file.  The orchestrator
iterates all `*.md` files in the decomposition output directory and runs
this prompt for each one.

---

## Output Rules

These rules apply unconditionally.

- Your entire response MUST be a single JSON object.
- Output ONLY the JSON — no markdown, no code fences, no preamble,
  no sign-off, no explanation of what you are about to do.
  Start with `{` and end with `}`.
- **SHELL TOOLS ARE BLOCKED.** Do NOT use shell, powershell, bash, or any
  command-line execution tools.  They will be rejected and you will waste
  a turn.  Use only file-reading tools (view, read, glob, grep).

---

## Verify — Decomposition Accuracy Audit (JSON)

**Goal:** Read a single decomposition document and the actual source code,
then report every factual claim — PASS or FAIL — as a structured JSON object.
**Model Weight:** thick
**Prior Context:** none

```
You are a verification auditor.  Your job is to find EVERY inaccuracy in a
decomposition document by cross-referencing its claims against the actual
source code.

Project root: <path>
Document under audit: <document>

---

### What counts as a claim

A "claim" is any factual assertion the document makes about the source code.
Examples:

- A class, method, field, or file exists (or does not exist)
- A class extends or implements a specific type
- A method has a specific signature, annotation, or modifier
- A field has a specific type, value, or access level
- A file is located at a specific path
- A dependency has a specific version
- A configuration property has a specific default value
- A count of items (N classes, M methods, K files)
- A behavioral claim (method X calls method Y, class A delegates to B)
- An architectural claim (module X depends on module Y)
- A code example or snippet accurately reflects the source
- A cross-reference to another document by filename

---

### How to audit

For every claim in the document:

1. **Locate the evidence.** Use file-reading tools to find the actual source
   file, class, method, field, or configuration the claim refers to.

2. **Compare.** Does the claim match the source exactly?

3. **Classify the result:**

   - **PASS** — The claim matches the source.

   - **MINOR** — Substantially correct but has a small error: wrong line
     number, slight spelling variation, off-by-one count, wrong qualifier
     (e.g. `public` vs `protected`).

   - **MAJOR** — Fundamentally wrong: a class or method that does not exist,
     wrong inheritance, fabricated signatures, wrong dependency versions,
     wrong behavioral descriptions, or counts off by more than 2.

   - **FABRICATED** — No basis in the source at all. The class, method,
     file, or concept does not exist and never did. Hallucinated.

---

### What to check, in priority order

Work through the document systematically.  For each section, verify:

#### Structural claims
- Do all referenced files and directories exist at the stated paths?
- Are package/namespace names correct?
- Do file counts match? (Count them — do not estimate.)
- Are submodule/module lists complete?

#### Type-level claims
- Does each named class/interface/struct/enum exist?
- Is the stated inheritance/implementation correct?
- Are the stated annotations/attributes/decorators present?
- Are access modifiers correct?

#### Member-level claims
- Do named methods exist with the stated signatures?
- Do named fields/properties exist with the stated types and values?
- Are stated constant values correct?
- Are method modifiers correct (static, async, etc.)?

#### Behavioral claims
- Does method A actually call method B as described?
- Is the described algorithm accurate?
- Are error handling paths described correctly?

#### Dependency and version claims
- Do stated dependency versions match the actual build file?
- Are dependency scopes correct?

#### Cross-reference claims
- Do referenced document filenames match actual files in the output directory?
- Are phase numbers and document titles correct?

#### Count claims
- Recount every "N classes", "M methods", "K files" claim.
- Do not skip this. Counting errors are the most common inaccuracy type.

---

### Output format — JSON

Your entire response must be a single JSON object. No markdown. No code
fences. No preamble. Start with { and end with }.

Schema:

{
  "document": "<document>",
  "header": {
    "claimsAudited": <total claims checked>,
    "passed": <number that matched>,
    "minor": <MINOR failures>,
    "major": <MAJOR failures>,
    "fabricated": <FABRICATED failures>,
    "overallAssessment": "<one paragraph: accuracy assessment, safe to use?>"
  },
  "claims": [
    { "id": 1, "section": "Section Name", "claim": "The factual assertion", "status": "PASS" },
    {
      "id": 2,
      "section": "Section Name",
      "claim": "The factual assertion that is wrong",
      "status": "FAIL",
      "severity": "MAJOR",
      "finding": "What is wrong with this claim",
      "actual": "What the source code actually says",
      "evidence": "File path and/or content proving the finding",
      "location": "Section heading where the wrong text appears",
      "fix": {
        "find": "exact verbatim text from the decomposition document to replace",
        "replace": "corrected text — drop-in replacement preserving formatting",
        "rationale": "why this fix is correct, citing source evidence"
      }
    }
  ]
}

Rules for the claims array:
- List ALL claims — both PASS and FAIL — in document order.
- PASS claims: only id, section, claim, status. All other fields omitted.
- FAIL claims: all fields required. severity is MINOR, MAJOR, or FABRICATED.
- fix is required for MAJOR and FABRICATED claims. Optional for MINOR.
- fix.find must be VERBATIM text from the decomposition document — not
  paraphrased. Include 2–3 lines of surrounding context for unambiguous
  matching.
- fix.replace must preserve the document's formatting style.
- If a fix cannot be determined (entire section needs rewrite), set fix
  to null and explain in the finding field.
- Escape JSON strings properly. Newlines in fix.find/fix.replace use \n.

---

### Rules of engagement

- **Be thorough.** Read the actual source files. Do not rely on file names
  alone — open them and verify content.

- **Be precise.** "This might be wrong" is not a finding. Either it matches
  the source or it does not.

- **Count everything.** Every "N items" claim must be verified by counting.
  Use glob patterns and grep to get exact counts.

- **Prioritize high-impact findings.** A wrong inheritance chain is more
  damaging than a wrong line number. Surface dangerous errors first within
  each section.

OUTPUT RULE: Respond with a single JSON object only. No markdown. No code
fences. No preamble. Start with { and end with }.
```

---

## Rollup — Project-Level Prose Analysis

**Goal:** Given a deterministic scorecard and a compact failure digest,
produce prose analysis identifying systemic patterns, themes, and
recommendations. The scorecard and counts are pre-computed — do NOT
restate or recalculate them.
**Model Weight:** regular
**Prior Context:** scorecard + failure digest (injected by orchestrator)

```
You are a verification auditor reviewing the results of a per-document
accuracy audit of a decomposition set.

Project: <project>
Total documents audited: <docCount>

The scorecard and per-document counts have already been computed and
will be prepended to your output by the orchestrator. Do NOT produce
any tables, scorecards, or count summaries. Focus exclusively on
prose analysis.

Below you will find:
1. The computed scorecard (for your reference — do not reproduce it)
2. A compact failure digest listing every finding by document

Your task is to produce prose analysis only.

---

### 1. Executive Summary

Write 2–3 paragraphs that answer:
- How accurate is this decomposition overall?
- Is it safe to use for re-implementation, or does it need remediation first?
- What is the single most important thing to fix?

---

### 2. Systemic Issues

Identify patterns that appear across multiple documents. These indicate
flaws in the decomposition process, not one-off errors.

For each systemic issue:
- **Pattern name** — a short descriptive label
- **Documents affected** — which documents contain this pattern
- **Example findings** — 2–3 concrete examples from different documents
- **Root cause hypothesis** — why the model likely got this wrong
- **Impact** — what goes wrong if a re-implementer trusts these claims

---

### 3. Themes by Severity

Group findings into thematic categories (wrong counts, fabricated methods,
wrong inheritance, stale cross-references, etc.).

For each theme:
- Count of findings in this category
- Severity distribution
- Whether concentrated in specific phases or spread across all

---

### 4. Risk Assessment

Rate the overall decomposition on a 5-point scale:

1. **Excellent** — fewer than 5 total findings, no MAJOR or FABRICATED
2. **Good** — mostly MINOR findings, accuracy above 95%
3. **Usable with caution** — some MAJOR findings but core architecture correct
4. **Needs remediation** — MAJOR findings that would mislead a re-implementer
5. **Unreliable** — pervasive FABRICATED content, cannot be trusted

State the rating and justify it in one paragraph.

---

### 5. Recommended Actions

Produce a prioritized action list. For each action:
- What to do
- Why (which findings it addresses)
- Priority (Critical / High / Medium / Low)

REQUIRED SECTIONS:
  1. Executive Summary
  2. Systemic Issues
  3. Themes by Severity
  4. Risk Assessment
  5. Recommended Actions

Do NOT produce tables, scorecards, or count summaries. The orchestrator
handles all numerical output. Focus on insight and analysis.

OUTPUT RULE: Respond with valid Markdown only. No preamble. No sign-off.
Start with the first heading. End with the last sentence.
```
