# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Two NuGet packages, one solution (`XKit.Redactor.slnx`), one test project. `README.md` is the package readme and the design record - read it first; it explains the null/empty contract, why `Mask` never applies to a composite span, and which redactor belongs where.

- `XKit.Redactor/` - the contract and the cheap rule-based `CredentialRedactor`. Targets down to netstandard2.0, so no ranges/indices on strings, no `string.Contains(string, StringComparison)`, and the nullable attributes come from `NullableAttributes.cs` under `#if NETSTANDARD2_0`.
- `XKit.Redactor.Implementation/` - `EntropyRedactor` and `WordDictionary`. net8.0+ only, because it leans on spans, `CollectionsMarshal` and `Convert.FromHexString`.
- `XKit.Redactor.Tests/` - NUnit. `testconfigdata.json` is a dump of a real development environment with expected redactions beside the entries that change; it is the regression suite the entropy detector was tuned against. Keys ending in `$` are expectations, and `***` there means "the whole value is hidden".

## Code style (the owner's rules, shared with the other Xkit and ApexTroid repos)

- **Maintainability is priority #1** in any style or design decision.
- **Avoid `static`.** Even a method that touches no instance state should not be static. Extension members and immutable data tables (the secret-key markers) are the accepted exceptions; compiled regexes live in instance fields.
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

No CI. Publishing is a manual `dotnet nuget push` of both packages after bumping `<Version>` in each csproj.

## Things to keep

- The per-token dictionary pass in `WordDictionary.StripKnownWords` replaced one `string.Replace` per dictionary word per token. The perf guard in `EntropyRedactorTests` is coarse on purpose; do not tighten it into a flaky benchmark.
- The entropy thresholds (3.3, 2.9 under a credential-shaped key) and the hex length bump are tuned against the fixture. A change that moves any fixture case needs a reason written into the test.
