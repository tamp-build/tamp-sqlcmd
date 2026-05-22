# Changelog

All notable changes to `Tamp.SqlCmd` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] — Unreleased

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
