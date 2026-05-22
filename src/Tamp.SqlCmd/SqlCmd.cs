namespace Tamp.SqlCmd;

/// <summary>
/// Typed wrappers for the cross-platform <c>sqlcmd</c> CLI (<a href="https://github.com/microsoft/go-sqlcmd">go-sqlcmd</a>).
/// v1 covers the two verbs that drive CI database deploys: <c>RunScript</c> (execute one or more <c>.sql</c>
/// files) and <c>RunInline</c> (execute an inline T-SQL string).
/// </summary>
/// <remarks>
/// <para>
/// The wrapper emits the legacy-style direct-flag CLI shape (<c>sqlcmd -S srv -d db -U u -P p -i file.sql -b</c>),
/// not the modern subcommand shape (<c>sqlcmd query "..."</c>). The legacy shape is what CI / deploy adopters
/// reach for and matches the long-tail of sqlcmd documentation in the wild.
/// </para>
/// <para>
/// Idempotency tracking (the <c>__Migrations</c> table pattern, ordering, dependency resolution) is
/// intentionally adopter-side — this wrapper just executes. Compose with adopter-owned logic for
/// migration discipline.
/// </para>
/// <code>
/// [FromPath("sqlcmd")] readonly Tool SqlCmd = null!;
/// [Parameter] readonly Secret DbPassword = null!;
///
/// Target Migrate => _ => _.Executes(() => SqlCmd.RunScript(SqlCmd, s => s
///     .SetServer("sql.example.com")
///     .SetDatabase("AppDb")
///     .SetUserName("deploy")
///     .SetPassword(DbPassword)
///     .SetTrustServerCertificate()
///     .SetExitOnError()
///     .AddInputFile("migrations/001_init.sql")
///     .AddInputFile("migrations/002_seed.sql")));
/// </code>
/// </remarks>
public static class SqlCmd
{
    /// <summary><c>sqlcmd -i file.sql [-i file2.sql ...]</c> — execute one or more T-SQL script files.</summary>
    public static CommandPlan RunScript(Tool tool, Action<RunScriptSettings> configure)
        => Run<RunScriptSettings>(tool, configure);

    /// <summary><c>sqlcmd -Q "SELECT ..."</c> — execute an inline T-SQL statement.</summary>
    public static CommandPlan RunInline(Tool tool, Action<RunInlineSettings> configure)
        => Run<RunInlineSettings>(tool, configure);

    // ---- Object-init overloads ----
    public static CommandPlan RunScript(Tool tool, RunScriptSettings settings) => Plan(tool, settings);
    public static CommandPlan RunInline(Tool tool, RunInlineSettings settings) => Plan(tool, settings);

    private static CommandPlan Run<T>(Tool tool, Action<T>? configure) where T : SqlCmdSettingsBase, new()
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        var s = new T();
        configure?.Invoke(s);
        return s.ToCommandPlan(tool);
    }

    private static CommandPlan Plan<T>(Tool tool, T settings) where T : SqlCmdSettingsBase
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        return settings.ToCommandPlan(tool);
    }
}
