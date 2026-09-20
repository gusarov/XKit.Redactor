# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Two NuGet packages, one solution (`XKit.Redactor.slnx`), one test project. `README.md` is the package readme and the design record - read it first; it explains the null/empty contract, why `Mask` never applies to a composite span, and which redactor belongs where.

- `XKit.Redactor/` - the contract and the cheap rule-based `CredentialRedactor`. Targets down to netstandard2.0, so no ranges/indices on strings, no `string.Contains(string, StringComparison)`, and the nullable attributes come from `NullableAttributes.cs` under `#if NETSTANDARD2_0`.
- `XKit.Redactor.Implementation/` - `EntropyRedactor` and `WordDictionary`. net8.0+ only, because it leans on spans, `CollectionsMarshal` and `Convert.FromHexString`.
- `XKit.Redactor.Tests/` - NUnit. `testconfigdata.json` is a dump of a real development environment with expected redactions beside the entries that change; it is the regression suite the entropy detector was tuned against. Keys ending in `$` are expectations, and `***` there means "the whole value is hidden".

## Code style (the owner's rules, shared with the other Xkit and ApexTroid repos)

- **Maintainability is priority #1** in any style or design decision.
- **Avoid `static`.** Even a method that touches no instance state should not be static. Extension members and immutable data tables (the secret-key markers) are the accepted exceptions; compiled regexes live in instance fields. `CredentialRedactor.Default` is the one shared instance, and it exists only because `RedactedException` has no composition root to ask — constructing a redactor compiles three regexes, which cannot happen per exception.
- **Tabs for indentation.** Project files use two spaces.
- **Format inline collections/arguments so any single line is copy/paste-able.** Trailing commas; leading commas where trailing ones are not allowed; the closing bracket never shares a line with a parameter.
- Doc comments say *why*, in the voice of the existing ones. Every public member has one (`GenerateDocumentationFile` is on, and CS1591 is a warning to fix).

## Commands

```bash
dotnet test XKit.Redactor.slnx
```

```bash
dotnet pack XKit.Redactor.slnx -c Release
```

**Publishing is automatic and a push to `master` is a release.** The `XKit.Redactor` pipeline (definition 119) in the `GitHubNugets` Azure DevOps project builds, tests, packs and `dotnet nuget push`es both packages to nuget.org on every `master` commit, with `--skip-duplicate`. So bump `<Version>` in **both** csproj files before pushing, in lockstep, or the commit ships nothing. A published version cannot be replaced.

## Things to keep

- The per-token dictionary pass in `WordDictionary.StripKnownWords` replaced one `string.Replace` per dictionary word per token. The perf guard in `EntropyRedactorTests` is coarse on purpose; do not tighten it into a flaky benchmark.
- The entropy thresholds (3.3, 2.9 under a credential-shaped key) and the hex length bump are tuned against the fixture. A change that moves any fixture case needs a reason written into the test.
- `RedactedException` deliberately has no constructor without an inner exception, and `ToString()` is a *sealed* override with no opt-out. Both are guarantees, not defaults — `RedactedExceptionTests` asserts each by reflection so removing one fails rather than silently weakening the type. The owner's `exception-handling` skill is where the reasoning lives.
