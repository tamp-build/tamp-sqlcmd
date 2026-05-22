using System.Linq;
using Bogus;
using Tamp;
using Tamp.SqlCmd;
using Xunit;

namespace Tamp.SqlCmd.Tests;

public sealed class SqlCmdTests
{
    private static readonly string FakeToolPath = OperatingSystem.IsWindows()
        ? "C:\\fake\\sqlcmd.exe"
        : "/fake/sqlcmd";

    private static Tool FakeTool() => new(AbsolutePath.Create(FakeToolPath));

    private static Secret FakePw(string name = "DB_PW") => new(name, "p@ssw0rd!");

    private static int IndexOf(IReadOnlyList<string> args, string token)
    {
        for (var i = 0; i < args.Count; i++) if (args[i] == token) return i;
        return -1;
    }

    // ---- Connection: server / database ----

    [Fact]
    public void RunScript_Emits_Server_And_Input_File()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("sql.example.com")
            .AddInputFile("migrations/001.sql"));

        var sIdx = IndexOf(plan.Arguments, "-S");
        Assert.True(sIdx >= 0, "-S missing");
        Assert.Equal("sql.example.com", plan.Arguments[sIdx + 1]);
        var iIdx = IndexOf(plan.Arguments, "-i");
        Assert.True(iIdx >= 0, "-i missing");
        Assert.Equal("migrations/001.sql", plan.Arguments[iIdx + 1]);
    }

    [Fact]
    public void RunScript_Emits_Database()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("sql.example.com")
            .SetDatabase("AppDb")
            .AddInputFile("a.sql"));

        var dIdx = IndexOf(plan.Arguments, "-d");
        Assert.True(dIdx >= 0);
        Assert.Equal("AppDb", plan.Arguments[dIdx + 1]);
    }

    [Fact]
    public void RunScript_Without_Server_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SqlCmd.RunScript(FakeTool(), s => s
            .AddInputFile("a.sql")));
        Assert.Contains("Server", ex.Message);
    }

    [Fact]
    public void RunScript_With_Server_Env_Var_Skips_Required_Check()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetEnvironmentVariable("SQLCMDSERVER", "sql.env.example")
            .AddInputFile("a.sql"));
        // Doesn't throw — env var fills in.
        Assert.NotNull(plan);
    }

    // ---- Authentication modes ----

    [Fact]
    public void Username_Password_Auth_Emits_U_P_Flags()
    {
        var pw = FakePw();
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetUserName("deploy")
            .SetPassword(pw)
            .AddInputFile("a.sql"));

        var uIdx = IndexOf(plan.Arguments, "-U");
        var pIdx = IndexOf(plan.Arguments, "-P");
        Assert.Equal("deploy", plan.Arguments[uIdx + 1]);
        Assert.Equal("p@ssw0rd!", plan.Arguments[pIdx + 1]);
    }

    [Fact]
    public void Password_Tracked_As_Secret()
    {
        var pw = FakePw("PROD_PW");
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetUserName("deploy")
            .SetPassword(pw)
            .AddInputFile("a.sql"));

        Assert.Contains(plan.Secrets, x => ReferenceEquals(x, pw));
    }

    [Fact]
    public void TrustedConnection_Emits_E_Flag_No_UserName()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .AddInputFile("a.sql"));

        Assert.Contains("-E", plan.Arguments);
        Assert.DoesNotContain("-U", plan.Arguments);
        Assert.DoesNotContain("-P", plan.Arguments);
    }

    [Fact]
    public void TrustedConnection_Plus_Username_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .SetUserName("u")
            .SetPassword(FakePw())
            .AddInputFile("a.sql")));
        Assert.Contains("one authentication mode", ex.Message);
    }

    [Fact]
    public void Username_Without_Password_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetUserName("deploy")
            .AddInputFile("a.sql")));
        Assert.Contains("Password", ex.Message);
    }

    [Fact]
    public void Password_Without_Username_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetPassword(FakePw())
            .AddInputFile("a.sql")));
        Assert.Contains("UserName", ex.Message);
    }

    [Fact]
    public void AuthenticationMethod_Emits_Aad_Flag()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv.database.windows.net")
            .SetAuthenticationMethod("ActiveDirectoryDefault")
            .AddInputFile("a.sql"));

        var idx = IndexOf(plan.Arguments, "--authentication-method");
        Assert.Equal("ActiveDirectoryDefault", plan.Arguments[idx + 1]);
    }

    [Fact]
    public void AuthenticationMethod_Plus_TrustedConnection_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetAuthenticationMethod("ActiveDirectoryDefault")
            .SetTrustedConnection()
            .AddInputFile("a.sql")));
    }

    // ---- TLS knobs ----

    [Fact]
    public void TrustServerCertificate_Emits_C()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .SetTrustServerCertificate()
            .AddInputFile("a.sql"));

        Assert.Contains("-C", plan.Arguments);
    }

    [Theory]
    [InlineData(SqlCmdEncryption.Optional, "optional")]
    [InlineData(SqlCmdEncryption.Mandatory, "mandatory")]
    [InlineData(SqlCmdEncryption.Strict, "strict")]
    public void Encryption_Mode_Emits_N_With_Value(SqlCmdEncryption mode, string expected)
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .SetEncryption(mode)
            .AddInputFile("a.sql"));

        var idx = IndexOf(plan.Arguments, "-N");
        Assert.Equal(expected, plan.Arguments[idx + 1]);
    }

    [Fact]
    public void Encryption_Default_Omits_N()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .AddInputFile("a.sql"));
        Assert.DoesNotContain("-N", plan.Arguments);
    }

    // ---- Input files (multiple) ----

    [Fact]
    public void RunScript_Multiple_InputFiles_All_Emit_With_Repeated_i_Flag()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .AddInputFile("001.sql")
            .AddInputFile("002.sql")
            .AddInputFile("003.sql"));

        // Each file gets its own -i prefix
        var iCount = plan.Arguments.Count(a => a == "-i");
        Assert.Equal(3, iCount);
        Assert.Contains("001.sql", plan.Arguments);
        Assert.Contains("002.sql", plan.Arguments);
        Assert.Contains("003.sql", plan.Arguments);
    }

    [Fact]
    public void RunScript_SetInputFiles_Replaces_Existing()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .AddInputFile("old.sql")
            .SetInputFiles("new.sql"));

        Assert.DoesNotContain("old.sql", plan.Arguments);
        Assert.Contains("new.sql", plan.Arguments);
    }

    [Fact]
    public void RunScript_Without_InputFiles_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()));
    }

    // ---- RunInline verb ----

    [Fact]
    public void RunInline_Emits_Q_Flag_With_Query()
    {
        var plan = SqlCmd.RunInline(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .SetQuery("SELECT @@VERSION;"));

        var idx = IndexOf(plan.Arguments, "-Q");
        Assert.Equal("SELECT @@VERSION;", plan.Arguments[idx + 1]);
    }

    [Fact]
    public void RunInline_Without_Query_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SqlCmd.RunInline(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()));
        Assert.Contains("Query", ex.Message);
    }

    // ---- Variables ----

    [Fact]
    public void Variables_Emit_v_With_NameEqualsValue_Pairs()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .AddInputFile("a.sql")
            .SetVariable("Env", "prod")
            .SetVariable("Tenant", "acme"));

        // Two separate -v flags, each followed by Name=Value
        var vIndices = Enumerable.Range(0, plan.Arguments.Count)
            .Where(i => plan.Arguments[i] == "-v")
            .ToList();
        Assert.Equal(2, vIndices.Count);
        var values = vIndices.Select(i => plan.Arguments[i + 1]).ToHashSet();
        Assert.Contains("Env=prod", values);
        Assert.Contains("Tenant=acme", values);
    }

    [Fact]
    public void DisableVariableSubstitution_Emits_x_Flag()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .AddInputFile("a.sql")
            .SetDisableVariableSubstitution());
        Assert.Contains("-x", plan.Arguments);
    }

    // ---- Error handling / timeouts ----

    [Fact]
    public void ExitOnError_Default_True_Emits_b()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .AddInputFile("a.sql"));
        Assert.Contains("-b", plan.Arguments);
    }

    [Fact]
    public void ExitOnError_False_Omits_b()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .AddInputFile("a.sql")
            .SetExitOnError(false));
        Assert.DoesNotContain("-b", plan.Arguments);
    }

    [Fact]
    public void QueryTimeoutSeconds_Emits_t_Flag()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .AddInputFile("a.sql")
            .SetQueryTimeoutSeconds(300));

        var idx = IndexOf(plan.Arguments, "-t");
        Assert.Equal("300", plan.Arguments[idx + 1]);
    }

    [Theory]
    [InlineData(0, "-r0")]
    [InlineData(1, "-r1")]
    public void ErrorsToStderr_Emits_r_Flag(int mode, string expected)
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .AddInputFile("a.sql")
            .SetErrorsToStderr(mode));
        Assert.Contains(expected, plan.Arguments);
    }

    // ---- Output ----

    [Fact]
    public void OutputFile_Emits_o_Flag()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .AddInputFile("a.sql")
            .SetOutputFile("artifacts/results.out"));

        var idx = IndexOf(plan.Arguments, "-o");
        Assert.Equal("artifacts/results.out", plan.Arguments[idx + 1]);
    }

    // ---- Cross-cutting ----

    [Fact]
    public void Executable_Is_Tool_Path()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .AddInputFile("a.sql"));
        Assert.Equal(FakeToolPath, plan.Executable);
    }

    [Fact]
    public void Working_Directory_Propagates()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .AddInputFile("a.sql")
            .SetWorkingDirectory("/tmp/build"));
        Assert.Equal("/tmp/build", plan.WorkingDirectory);
    }

    [Fact]
    public void Environment_Variables_Propagate()
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .AddInputFile("a.sql")
            .SetEnvironmentVariable("SQLCMDMAXVARTYPEWIDTH", "8000"));
        Assert.Equal("8000", plan.Environment["SQLCMDMAXVARTYPEWIDTH"]);
    }

    // ---- Object-init parity ----

    [Fact]
    public void RunScript_ObjectInit_Equivalent_To_Fluent()
    {
        var pw = FakePw();
        var fluent = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetDatabase("AppDb")
            .SetUserName("deploy")
            .SetPassword(pw)
            .SetTrustServerCertificate()
            .SetEncryption(SqlCmdEncryption.Mandatory)
            .AddInputFile("001.sql")
            .AddInputFile("002.sql")
            .SetVariable("Env", "prod")
            .SetQueryTimeoutSeconds(120));

        var settings = new RunScriptSettings
        {
            Server = "srv",
            Database = "AppDb",
            UserName = "deploy",
            Password = pw,
            TrustServerCertificate = true,
            Encryption = SqlCmdEncryption.Mandatory,
            QueryTimeoutSeconds = 120,
        };
        settings.InputFiles.AddRange(new[] { "001.sql", "002.sql" });
        settings.Variables["Env"] = "prod";
        var objInit = SqlCmd.RunScript(FakeTool(), settings);

        Assert.Equal(fluent.Arguments, objInit.Arguments);
    }

    // ---- Realistic CI invocation ----

    [Fact]
    public void Realistic_CI_Migration_Invocation()
    {
        var pw = FakePw("PROD_PW");
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("sql-prod.example.com,1433")
            .SetDatabase("AppDb")
            .SetUserName("deploy")
            .SetPassword(pw)
            .SetTrustServerCertificate()
            .SetEncryption(SqlCmdEncryption.Mandatory)
            .AddInputFile("migrations/001_schema.sql")
            .AddInputFile("migrations/002_seed.sql")
            .SetVariable("DeployTag", "v1.2.3")
            .SetQueryTimeoutSeconds(600));

        // Connection
        var sIdx = IndexOf(plan.Arguments, "-S");
        Assert.Equal("sql-prod.example.com,1433", plan.Arguments[sIdx + 1]);
        var dIdx = IndexOf(plan.Arguments, "-d");
        Assert.Equal("AppDb", plan.Arguments[dIdx + 1]);

        // Auth
        Assert.Contains("-U", plan.Arguments);
        Assert.Contains("-P", plan.Arguments);
        Assert.Contains("-C", plan.Arguments);
        Assert.Contains("-N", plan.Arguments);

        // Two input files
        Assert.Equal(2, plan.Arguments.Count(a => a == "-i"));

        // Variable
        Assert.Contains("DeployTag=v1.2.3", plan.Arguments);

        // Behavior
        Assert.Contains("-b", plan.Arguments);
        Assert.Contains("600", plan.Arguments);

        // Secret tracked
        Assert.Contains(plan.Secrets, x => ReferenceEquals(x, pw));
    }

    // ---- Boundary / fuzz ----

    [Theory]
    [InlineData("path with spaces/migration.sql")]
    [InlineData("migrations/Δ-π/001.sql")]
    [InlineData("migrations/sub'dir/001.sql")]
    public void InputFile_Path_Roundtrips_Verbatim(string path)
    {
        var plan = SqlCmd.RunScript(FakeTool(), s => s
            .SetServer("srv")
            .SetTrustedConnection()
            .AddInputFile(path));
        Assert.Contains(path, plan.Arguments);
    }

    [Fact]
    public void Bulk_Variables_All_Emit()
    {
        var faker = new Faker();
        var pairs = Enumerable.Range(0, 30)
            .Select(_ => (Name: faker.Hacker.Noun() + faker.Random.AlphaNumeric(4), Value: faker.Random.Word()))
            .GroupBy(p => p.Name).Select(g => g.First())
            .ToList();

        var plan = SqlCmd.RunScript(FakeTool(), s =>
        {
            s.SetServer("srv").SetTrustedConnection().AddInputFile("a.sql");
            foreach (var (n, v) in pairs) s.SetVariable(n, v);
        });

        foreach (var (n, v) in pairs)
        {
            Assert.Contains($"{n}={v}", plan.Arguments);
        }
    }
}
