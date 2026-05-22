namespace Tamp.SqlCmd;

/// <summary>
/// TLS encryption mode for the sqlcmd connection (<c>-N</c> / <c>--encrypt-connection</c>).
/// </summary>
public enum SqlCmdEncryption
{
    /// <summary>Omit the <c>-N</c> flag (sqlcmd default).</summary>
    Default,
    /// <summary>Optional encryption — connect over TLS if available, fall back otherwise.</summary>
    Optional,
    /// <summary>Mandatory encryption — fail if TLS isn't available.</summary>
    Mandatory,
    /// <summary>Strict encryption — TLS plus full certificate validation.</summary>
    Strict,
}

/// <summary>
/// Common knobs shared across sqlcmd verbs: connection settings, encryption, output redirect,
/// SqlCmd variables, exit-on-error, query timeout. Per-verb subclasses contribute either
/// <c>-i file.sql</c> (RunScript) or <c>-Q "..."</c> (RunInline).
/// </summary>
public abstract class SqlCmdSettingsBase
{
    /// <summary>Working directory for the spawned <c>sqlcmd</c> process.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Per-invocation environment variables.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new();

    /// <summary>SQL Server endpoint (<c>-S</c>). Required unless reading from <c>SQLCMDSERVER</c> env var.</summary>
    public string? Server { get; set; }

    /// <summary>Initial database (<c>-d</c>).</summary>
    public string? Database { get; set; }

    /// <summary>Username for SQL auth (<c>-U</c>).</summary>
    public string? UserName { get; set; }

    /// <summary>Password for SQL auth (<c>-P</c>). Tracked as <see cref="Secret"/>.</summary>
    public Secret? Password { get; set; }

    /// <summary>Use Windows integrated auth (<c>-E</c>). Mutually exclusive with username/password.</summary>
    public bool TrustedConnection { get; set; }

    /// <summary>Azure AD authentication method (<c>--authentication-method</c>) — e.g. <c>"ActiveDirectoryDefault"</c>, <c>"ActiveDirectoryServicePrincipal"</c>.</summary>
    public string? AuthenticationMethod { get; set; }

    /// <summary>Trust the server's TLS certificate without validation (<c>-C</c>).</summary>
    public bool TrustServerCertificate { get; set; }

    /// <summary>TLS encryption mode (<c>-N</c>).</summary>
    public SqlCmdEncryption Encryption { get; set; } = SqlCmdEncryption.Default;

    /// <summary>Output file for query results (<c>-o</c>).</summary>
    public string? OutputFile { get; set; }

    /// <summary>SqlCmd scripting variables (<c>-v Name=Value</c>). Repeatable.</summary>
    public Dictionary<string, string> Variables { get; } = new();

    /// <summary>Exit with non-zero status on T-SQL error (<c>-b</c>). Default true (CI-friendly).</summary>
    public bool ExitOnError { get; set; } = true;

    /// <summary>Disable scripting variable substitution (<c>-x</c>) — useful when scripts contain literal <c>$(...)</c> tokens.</summary>
    public bool DisableVariableSubstitution { get; set; }

    /// <summary>Query timeout in seconds (<c>-t</c>).</summary>
    public int? QueryTimeoutSeconds { get; set; }

    /// <summary>Redirect error messages with severity ≥ 11 to stderr (<c>-r0</c>) or all errors to stderr (<c>-r1</c>). <c>null</c> omits.</summary>
    public int? ErrorsToStderr { get; set; }

    /// <summary>Subclasses produce the verb-specific arguments (<c>-i</c> input files or <c>-Q</c> inline query).</summary>
    protected abstract IEnumerable<string> BuildVerbArguments();

    /// <summary>Per-verb validation hook.</summary>
    protected virtual void Validate()
    {
        if (string.IsNullOrEmpty(Server) && !EnvironmentVariables.ContainsKey("SQLCMDSERVER"))
            throw new InvalidOperationException("Server is required (set via SetServer, or pass SQLCMDSERVER env var).");

        var authsSet = (UserName is not null || Password is not null ? 1 : 0)
                     + (TrustedConnection ? 1 : 0)
                     + (AuthenticationMethod is not null ? 1 : 0);
        if (authsSet > 1)
            throw new InvalidOperationException("Choose one authentication mode: username/password OR TrustedConnection OR AuthenticationMethod.");

        if (UserName is not null && Password is null)
            throw new InvalidOperationException("UserName was set but Password was not.");
        if (Password is not null && UserName is null)
            throw new InvalidOperationException("Password was set but UserName was not.");
    }

    /// <summary>Subclasses extending the secret list (Password is collected here).</summary>
    protected virtual IEnumerable<Secret> CollectSecrets()
    {
        if (Password is not null) yield return Password;
    }

    internal CommandPlan ToCommandPlan(Tool tool)
    {
        Validate();

        var args = new List<string>();

        // Connection.
        if (!string.IsNullOrEmpty(Server)) { args.Add("-S"); args.Add(Server!); }
        if (!string.IsNullOrEmpty(Database)) { args.Add("-d"); args.Add(Database!); }

        if (TrustedConnection)
        {
            args.Add("-E");
        }
        else if (!string.IsNullOrEmpty(UserName))
        {
            args.Add("-U"); args.Add(UserName!);
            args.Add("-P"); args.Add(Password!.Reveal());
        }

        if (!string.IsNullOrEmpty(AuthenticationMethod))
        {
            args.Add("--authentication-method"); args.Add(AuthenticationMethod!);
        }

        if (TrustServerCertificate) args.Add("-C");
        if (Encryption != SqlCmdEncryption.Default)
        {
            args.Add("-N");
            args.Add(Encryption switch
            {
                SqlCmdEncryption.Optional => "optional",
                SqlCmdEncryption.Mandatory => "mandatory",
                SqlCmdEncryption.Strict => "strict",
                _ => throw new InvalidOperationException($"Unhandled SqlCmdEncryption: {Encryption}"),
            });
        }

        // Verb-specific (input files or inline query).
        foreach (var a in BuildVerbArguments()) args.Add(a);

        // Output.
        if (!string.IsNullOrEmpty(OutputFile)) { args.Add("-o"); args.Add(OutputFile!); }

        // Variables. sqlcmd's -v takes one or more Name=Value pairs as a single argument list,
        // but per the docs the safe shape is "-v" "Name1=Val1" repeated.
        foreach (var kv in Variables) { args.Add("-v"); args.Add($"{kv.Key}={kv.Value}"); }

        // Behavior flags.
        if (ExitOnError) args.Add("-b");
        if (DisableVariableSubstitution) args.Add("-x");
        if (QueryTimeoutSeconds is int t) { args.Add("-t"); args.Add(t.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        if (ErrorsToStderr is int r) args.Add($"-r{r}");

        return new CommandPlan
        {
            Executable = tool.Executable.Value,
            Arguments = args,
            Environment = new Dictionary<string, string>(EnvironmentVariables),
            WorkingDirectory = WorkingDirectory ?? tool.WorkingDirectory,
            Secrets = CollectSecrets().ToList(),
        };
    }
}

/// <summary>Fluent setters for the common knobs.</summary>
public static class SqlCmdSettingsBaseExtensions
{
    public static T SetWorkingDirectory<T>(this T s, string? cwd) where T : SqlCmdSettingsBase { s.WorkingDirectory = cwd; return s; }
    public static T SetServer<T>(this T s, string server) where T : SqlCmdSettingsBase { s.Server = server; return s; }
    public static T SetDatabase<T>(this T s, string? db) where T : SqlCmdSettingsBase { s.Database = db; return s; }
    public static T SetUserName<T>(this T s, string user) where T : SqlCmdSettingsBase { s.UserName = user; return s; }
    public static T SetPassword<T>(this T s, Secret password) where T : SqlCmdSettingsBase { s.Password = password; return s; }
    public static T SetTrustedConnection<T>(this T s, bool v = true) where T : SqlCmdSettingsBase { s.TrustedConnection = v; return s; }
    public static T SetAuthenticationMethod<T>(this T s, string? method) where T : SqlCmdSettingsBase { s.AuthenticationMethod = method; return s; }
    public static T SetTrustServerCertificate<T>(this T s, bool v = true) where T : SqlCmdSettingsBase { s.TrustServerCertificate = v; return s; }
    public static T SetEncryption<T>(this T s, SqlCmdEncryption mode) where T : SqlCmdSettingsBase { s.Encryption = mode; return s; }
    public static T SetOutputFile<T>(this T s, string? path) where T : SqlCmdSettingsBase { s.OutputFile = path; return s; }
    public static T SetVariable<T>(this T s, string name, string value) where T : SqlCmdSettingsBase { s.Variables[name] = value; return s; }
    public static T SetExitOnError<T>(this T s, bool v = true) where T : SqlCmdSettingsBase { s.ExitOnError = v; return s; }
    public static T SetDisableVariableSubstitution<T>(this T s, bool v = true) where T : SqlCmdSettingsBase { s.DisableVariableSubstitution = v; return s; }
    public static T SetQueryTimeoutSeconds<T>(this T s, int? seconds) where T : SqlCmdSettingsBase { s.QueryTimeoutSeconds = seconds; return s; }
    public static T SetErrorsToStderr<T>(this T s, int? mode) where T : SqlCmdSettingsBase { s.ErrorsToStderr = mode; return s; }
    public static T SetEnvironmentVariable<T>(this T s, string name, string value) where T : SqlCmdSettingsBase { s.EnvironmentVariables[name] = value; return s; }
}

/// <summary>
/// Settings for <c>sqlcmd -i file.sql [-i file2.sql ...]</c> — execute one or more script files.
/// </summary>
public sealed class RunScriptSettings : SqlCmdSettingsBase
{
    /// <summary>Input files (<c>-i</c>). At least one required.</summary>
    public List<string> InputFiles { get; } = new();

    public RunScriptSettings AddInputFile(string path) { InputFiles.Add(path); return this; }
    public RunScriptSettings SetInputFiles(params string[] paths) { InputFiles.Clear(); InputFiles.AddRange(paths); return this; }

    protected override void Validate()
    {
        base.Validate();
        if (InputFiles.Count == 0)
            throw new InvalidOperationException("RunScript requires at least one input file (set via AddInputFile / SetInputFiles).");
    }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        foreach (var f in InputFiles) { yield return "-i"; yield return f; }
    }
}

/// <summary>
/// Settings for <c>sqlcmd -Q "SELECT ..."</c> — execute an inline T-SQL statement and exit.
/// </summary>
public sealed class RunInlineSettings : SqlCmdSettingsBase
{
    /// <summary>Inline T-SQL to execute (<c>-Q</c>). Required.</summary>
    public string? Query { get; set; }

    public RunInlineSettings SetQuery(string query) { Query = query; return this; }

    protected override void Validate()
    {
        base.Validate();
        if (string.IsNullOrEmpty(Query))
            throw new InvalidOperationException("RunInline requires Query (set via SetQuery).");
    }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "-Q";
        yield return Query!;
    }
}
