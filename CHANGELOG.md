# Changelog

All notable changes to `Tamp.SqlCmd` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-11-20 - .NET 11 in, .NET 8 and .NET 9 out

### Changed

- **Multi-targets `net10.0;net11.0`** instead of `net8.0;net9.0;net10.0` ([ADR 0015](https://github.com/tamp-build/tamp/blob/main/docs/adr/0015-target-framework-strategy.md)). .NET 11 shipped 2026-11-10 and .NET 8 and .NET 9 reached end of support the same day; the rule is to add a TFM the day a release ships and drop one the day Microsoft ends its support. `global.json` pins SDK 11.0.100.

  **If you target `net8.0` or `net9.0`, stay on the previous version** — this one carries no assets your project can resolve.

- **`Tamp.*` pinned to 1.21.0**, which carries a SARIF fix worth knowing about if this project's output is read as evidence: before 1.21.0, a SARIF result whose scanner omitted `level` was read as `none` — the least severe value — instead of the declared `warning`, on every runtime before net11 ([tamp#166](https://github.com/tamp-build/tamp/issues/166)).

- **Publishing moved to nuget.org trusted publishing (OIDC).** The `tamp_nuget_api_key` org secret expired 2026-11-01 along with every other pre-2026-08-17 nuget.org key. The publish job now mints a short-lived key from its own OIDC token, so there is no long-lived credential to rotate or leak.

## [0.1.1] — 2026-09-27

### Added

- Initial release. Typed wrappers for the cross-platform `sqlcmd` CLI (go-sqlcmd) over the two highest-leverage verbs:
  - `RunScript` (`-i file.sql [-i file2.sql ...]`) — execute one or more T-SQL script files.
  - `RunInline` (`-Q "..."`) — execute an inline T-SQL statement.
- Connection settings: `-S server`, `-d database`, `-U user`, `-P password` (Secret-tracked).
- Three auth modes (mutually exclusive): SQL auth (user/password), Windows integrated (`-E`), Azure AD (`--authentication-method`).
- TLS knobs: `-C` trust-server-cert, `-N optional|mandatory|strict` encryption.
- SqlCmd variables (`-v Name=Value`, repeatable), output file (`-o`), query timeout (`-t`), exit-on-error (`-b`, default true), variable-substitution disable (`-x`), errors-to-stderr (`-r0`/`-r1`).
- Parallel fluent + object-init authoring surface.
- Multi-target `net8.0;net9.0;net10.0`.
- Package now ships XML documentation files (`.xml`) alongside the assembly, so consumers get IntelliSense and API docs. (Mirrors [tamp-build/tamp#3](https://github.com/tamp-build/tamp/pull/50).)
