// Copyright 2025 Code Philosophy

using System.Diagnostics;
using Luban.Tests.Helpers;
using Xunit;

namespace Luban.Tests.Integration;

[Collection("LubanPipeline")]
public class ErlangTests
{
    [Fact]
    public void Core_Erlang_RecordHeader_MatchesGolden()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("core"),
            codeTargets: new[] { "erlang-record" },
            dataTargets: new[] { "erlang" },
            outputCodeDir: ws.CodeDir,
            outputDataDir: ws.DataDir);
        GoldenAssert.AssertDirectoryMatches(ws.CodeDir, "core/erlang-record");
    }

    [Fact]
    public void Core_Erlang_ErlcCompileAndLoad()
    {
        string erlc = FindExecutable("erlc");
        string erl = FindExecutable("erl");
        if (erlc == null || erl == null)
        {
            // 本机未安装 Erlang 工具链时跳过编译验证
            return;
        }

        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("core"),
            codeTargets: new[] { "erlang-record" },
            dataTargets: new[] { "erlang" },
            outputCodeDir: ws.CodeDir,
            outputDataDir: ws.DataDir);

        string ebinDir = Path.Combine(ws.Root, "ebin");
        Directory.CreateDirectory(ebinDir);

        var compile = Run(erlc, new[]
        {
            "-Werror",
            "-I", ws.CodeDir,
            "-o", ebinDir,
        }.Concat(Directory.GetFiles(ws.DataDir, "*.erl")).ToArray());
        Assert.True(compile.ExitCode == 0, $"erlc failed:\n{compile.Stdout}\n{compile.Stderr}");

        const string evalCode = """
Item = tbitem:get(1),
{tbitem, 1, <<"sword"/utf8>>, 1, _, _, _, #{'__type__' := circle, id := 10, radius := 1.5}} = Item,
undefined = tbitem:get(99),
[1, 2] = lists:sort(tbitem:all()),
{tbglobal, 3, <<"demo"/utf8>>} = tbglobal:get(),
{tbscore, 1, 1, 100} = tbscore:get({1, 1}),
undefined = tbscore:get({9, 9}),
[{1, 1}, {1, 2}, {2, 1}] = lists:sort(tbscore:all()),
io:format("erlang check OK~n"),
halt(0).
""";

        var runResult = Run(erl, new[]
        {
            "-noshell",
            "-pa", ebinDir,
            "-eval", evalCode,
        });
        Assert.True(runResult.ExitCode == 0 && runResult.Stdout.Contains("erlang check OK"),
            $"erl runtime check failed:\n{runResult.Stdout}\n{runResult.Stderr}");
    }

    [Fact]
    public void Core_Erlang_AbstractValueTable_GeneratesAndCompiles()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("erlang"),
            codeTargets: new[] { "erlang-record" },
            dataTargets: new[] { "erlang" },
            outputCodeDir: ws.CodeDir,
            outputDataDir: ws.DataDir);
        Assert.Single(Directory.GetFiles(ws.DataDir, "*.erl"));
        Assert.Single(Directory.GetFiles(ws.CodeDir, "*.hrl"));

        string erlc = FindExecutable("erlc");
        string erl = FindExecutable("erl");
        if (erlc == null || erl == null)
        {
            // 本机未安装 Erlang 工具链时跳过编译验证
            return;
        }

        string ebinDir = Path.Combine(ws.Root, "ebin");
        Directory.CreateDirectory(ebinDir);

        var compile = Run(erlc, new[]
        {
            "-Werror",
            "-I", ws.CodeDir,
            "-o", ebinDir,
        }.Concat(Directory.GetFiles(ws.DataDir, "*.erl")).ToArray());
        Assert.True(compile.ExitCode == 0, $"erlc failed:\n{compile.Stdout}\n{compile.Stderr}");

        const string evalCode = """
{normal, 1, 10, #{'__type__' := circle, id := 5, time := {range, 100, 200}}} = tbabstract:get(1),
{equipment, 2, 99} = tbabstract:get(2),
undefined = tbabstract:get(3),
[1, 2] = lists:sort(tbabstract:all()),
io:format("erlang abstract check OK~n"),
halt(0).
""";

        var runResult = Run(erl, new[]
        {
            "-noshell",
            "-pa", ebinDir,
            "-eval", evalCode,
        });
        Assert.True(runResult.ExitCode == 0 && runResult.Stdout.Contains("erlang abstract check OK"),
            $"erl runtime check failed:\n{runResult.Stdout}\n{runResult.Stderr}");
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(string fileName, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }
        using var process = Process.Start(psi);
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);
        return (process.ExitCode, stdout, stderr);
    }

    private static string FindExecutable(string name)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var dir in paths)
        {
            string candidate = Path.Combine(dir.Trim(), name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}
