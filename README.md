# Tamp.SqlCmd

> Typed wrappers for the cross-platform `sqlcmd` CLI (Microsoft's [go-sqlcmd](https://github.com/microsoft/go-sqlcmd)). Run T-SQL scripts or inline queries against SQL Server and Azure SQL with typed connection settings, SqlCmd variable substitution, and `Secret`-tracked credentials.

| Package | Status |
|---|---|
| `Tamp.SqlCmd` | 0.1.0 (initial) |

## Install

```bash
dotnet add package Tamp.SqlCmd
```

Multi-targets net8 / net9 / net10. Requires `sqlcmd` on PATH:

- **macOS:** `brew install sqlcmd`
- **Linux:** `apt install sqlcmd` (or download from the [go-sqlcmd releases](https://github.com/microsoft/go-sqlcmd/releases))
- **Windows:** `winget install Microsoft.Sqlcmd` (or via SQL Server / Azure Data Studio installs)

## Quick start — run migration scripts

```csharp
using Tamp;
using Tamp.SqlCmd;

class Build : TampBuild
{
    public static int Main(string[] args) => Execute<Build>(args);

    [FromPath("sqlcmd")] readonly Tool SqlCmd = null!;
    [Parameter] readonly Secret DbPassword = null!;

    Target Migrate => _ => _.Executes(() => SqlCmd.RunScript(SqlCmd, s => s
        .SetServer("sql.example.com")
        .SetDatabase("AppDb")
        .SetUserName("deploy")
        .SetPassword(DbPassword)
        .SetTrustServerCertificate()
        .SetEncryption(SqlCmdEncryption.Mandatory)
        .AddInputFile("migrations/001_schema.sql")
        .AddInputFile("migrations/002_seed.sql")
        .SetVariable("DeployTag", "v1.2.3")
        .SetQueryTimeoutSeconds(600)));
}
```

## Verb surface (v1)

| Verb | Wraps | Required |
|---|---|---|
| `RunScript` | `sqlcmd -i file.sql [-i file2.sql ...]` | `Server` + at least one `InputFile` + an auth mode |
| `RunInline` | `sqlcmd -Q "SELECT ..."` | `Server` + `Query` + an auth mode |

## Authentication modes

Exactly one of these per invocation; the wrapper enforces it:

- **SQL auth** — `.SetUserName(u).SetPassword(secret)` → `-U u -P <secret>`
- **Windows integrated** — `.SetTrustedConnection()` → `-E`
- **Azure AD** — `.SetAuthenticationMethod("ActiveDirectoryDefault")` → `--authentication-method ActiveDirectoryDefault`

Setting more than one throws `InvalidOperationException`. Setting `UserName` without `Password` (or vice versa) throws.

## TLS knobs

| Setter | CLI |
|---|---|
| `.SetTrustServerCertificate()` | `-C` |
| `.SetEncryption(SqlCmdEncryption.Mandatory)` | `-N mandatory` |
| `.SetEncryption(SqlCmdEncryption.Strict)` | `-N strict` |

## Other common knobs

- **SqlCmd variables** — `.SetVariable(name, value)` → `-v Name=Value` (repeatable)
- **Exit on error** — `.SetExitOnError()` → `-b` (default true; CI-friendly)
- **Query timeout** — `.SetQueryTimeoutSeconds(300)` → `-t 300`
- **Output file** — `.SetOutputFile(path)` → `-o path`
- **Disable variable substitution** — `.SetDisableVariableSubstitution()` → `-x` (useful when scripts contain literal `$(...)` tokens)
- **Errors to stderr** — `.SetErrorsToStderr(0)` or `(1)` → `-r0` / `-r1`

## Settings authoring — fluent or object-init

Both styles produce identical `CommandPlan`s; fluent is canonical in docs.

## Out of scope

Idempotency tracking (the `__Migrations` table pattern, ordering, dependency resolution) is intentionally adopter-side. This wrapper just executes — compose with your own migration discipline.

## License

MIT — see [LICENSE](LICENSE).
