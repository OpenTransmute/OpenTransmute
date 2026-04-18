# Transmute — Helpers and Guards

This document contains standing rules that govern all agentic code generation
produced by the Transmute and Implement pipelines. Every rule applies regardless
of source language, target language, or plan structure.

---

## Guards for Agentic Code Generation

### Guard 1 — Read the full type definition before writing against it

Read the complete definition of any type before accessing, destructuring, or
pattern-matching it. Do not infer members from context, naming, or adjacent types.
Verify return types from the actual method signature — never from the method's name
or a similar API in another library.

### Guard 2 — Verify semantics in the target language

Do not carry source-language semantics into the target language. Verify every
operation — arithmetic, conversion, string handling, iteration, comparison — behaves
correctly in the target language. Write the target-correct form; never translate
source syntax literally.

### Guard 3 — Navigate wrapper types explicitly

Never assume a wrapper type exposes its inner type's members. Always access inner
members through the containing field. Do not infer promotion by naming similarity
or proximity.

### Guard 4 — Convert types explicitly

Never rely on implicit type conversion. Write an explicit conversion at every
operation boundary where types differ. When in doubt, convert explicitly.

### Guard 5 — Use correct literal types

Use the correct literal type for every value — do not interchange char and string,
integer and floating-point, or similar primitives. When calling an API with a
character or numeric argument, verify that the overload accepts your exact argument
type and all required parameters. Write an explicit conversion when no matching
overload exists.

### Guard 6 — Declare visibility explicitly and consistently

Every construct — type, function, method, module, or equivalent — must carry an
explicit visibility modifier. Never rely on language defaults. Any construct used
across a module, package, or assembly boundary must be exported with the correct
public modifier. Verify the full chain: a public API must not return or accept a
less-visible type, and types returned by public APIs must live in namespaces callers
can import.

### Guard 7 — Import names at the top; qualify ambiguous names

Place all imports, use, require, include, or equivalent declarations at the top of
each file before any definitions. Do not place imports inside functions, blocks, or
namespace bodies. Before referencing a type, verify its namespace is imported in the
current file. Do not name a type the same as its enclosing namespace. When a type
and its namespace share a name, use a global-alias qualifier or a using alias —
never rely on the short name. Consolidate shared imports into a project-wide file
where the language supports it.

### Guard 8 — Match callable signatures exactly

When assigning or passing a function as a value — delegate, callback, predicate, or
closure — the full parameter list and return type must match the target type exactly.
Optional or default parameters do not produce a compatible signature automatically.
Write an explicit adapter whenever signatures differ, including differences caused
solely by optional parameters.

### Guard 9 — Place doc comments only on valid targets

Place documentation comments only on constructs the target language accepts as valid
doc-comment targets. Import directives and other non-definitional statements are not
valid targets. Omit the comment entirely when the construct does not support it.

### Guard 10 — Use the most specific test assertion

In tests, always use the most specific assertion the framework provides for the
condition being checked. Do not use a generic boolean assertion when a dedicated one
exists — it produces less informative failure output and may be a build error when
the framework enforces specificity via static analysis. Use only the test framework
already referenced in the project's build file; do not mix frameworks.

### Guard 11 — Scope pattern variables correctly; annotate nullable types

A variable declared inside a pattern match is only assigned when the pattern
succeeds. Do not read it in any branch where the pattern may not have matched —
restructure into nested conditionals where necessary. Do not assign null to a
non-nullable variable; declare with a nullable annotation when null is a valid value
and propagate nullability through all downstream usages.

### Guard 12 — Co-locate type definitions with usage examples

Place a canonical usage example immediately adjacent to every non-trivial type
definition. Do not separate a type's definition from its intended usage pattern —
the model will infer the contract incorrectly when they are apart.

### Guard 13 — Always create project and build files

Always generate project, solution, makefile, or equivalent build artifacts for the
target ecosystem, even when the implementation plan does not mention them.

### Guard 14 — Never refuse; always begin implementing immediately

Never assess scope, decline, or ask for permission before starting. Do not produce
any response that says the task is too large, exceeds limits, or requires confirmation
before proceeding. Begin writing files immediately. If the full implementation cannot
fit in one interaction, implement incrementally in this fixed order: project and build
files first, then core types and interfaces, then services and business logic, then
tests, then CI and documentation. Continue until complete. Never stop and ask whether
to continue — always continue.

### Guard 16 — Split code by functional area; never write monolithic files

Organise source files by functional area. Each file must contain one cohesive
concern — a single type, a single service, a single functional boundary, or a small
cluster of tightly related helpers. Never place multiple unrelated types or
responsibilities in one file to save time. A file that grows past its single
responsibility must be split before writing continues. Entry-point and bootstrap
files are permitted to be thin orchestrators, but must not contain business logic.

### Guard 17 — Satisfy EARS requirements from the composition inventory

Before marking an implementation complete, verify every EARS requirement in the
composition inventory that applies to the implemented component. For each:
- Locate the EARS statement
- Identify the code path that satisfies it
- If no code path satisfies it, implement it or flag it explicitly as out of scope
Do not consider an implementation complete if any applicable EARS requirement is
unaddressed.
