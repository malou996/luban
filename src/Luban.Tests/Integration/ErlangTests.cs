// Copyright 2025 Code Philosophy

using System.Diagnostics;
using Luban.Diagnostics;
using Luban.Erlang;
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
none = tbitem:get(99),
[1, 2] = lists:sort(tbitem:all()),
{tbglobal, 3, <<"demo"/utf8>>} = tbglobal:get(),
{tbscore, 1, 1, 100} = tbscore:get({1, 1}),
none = tbscore:get({9, 9}),
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
{normal, 1, 10, #{'__type__' := circle, id := 5, time := {range, 100, 200}}, {range, 300, 400}} = tbabstract:get(1),
{equipment, 2, 99} = tbabstract:get(2),
none = tbabstract:get(3),
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

    [Fact]
    public void Core_Erlang_RecordFormat_FormattedRecord_IsDefault()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("core"),
            dataTargets: new[] { "erlang" },
            outputDataDir: ws.DataDir);

        string tbitem = File.ReadAllText(Path.Combine(ws.DataDir, "tbitem.erl"));
        // 默认 formatted_record：record 直接跟在 -> 后，每个字段独占一行、4 空格缩进，'}' 回到子句起始缩进
        Assert.Contains(
            "get(1) -> #tbitem{\n" +
            "    id = 1,\n" +
            "    name = <<\"sword\"/utf8>>,\n" +
            "    quality = 1,\n", tbitem);
        // 抽象 bean 字段保持带 __type__ 的单行 map
        Assert.Contains("shape = #{'__type__' => circle, id => 10, radius => 1.5}\n};", tbitem);
        Assert.Contains("get(_) -> none.", tbitem);
    }

    [Fact]
    public void Core_Erlang_RecordFormat_TagTakesPriorityOverXargsFallback()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("core"),
            dataTargets: new[] { "erlang" },
            outputDataDir: ws.DataDir,
            extraOptions: new Dictionary<string, string> { ["erlang.recordFormat"] = "online_record" });

        // TbItem 未配置 tags，回退到 xargs 的 online_record
        string tbitem = File.ReadAllText(Path.Combine(ws.DataDir, "tbitem.erl"));
        Assert.Contains("get(1) -> #tbitem{id = 1, name = <<\"sword\"/utf8>>, quality = 1,", tbitem);
        // TbScore 的 tags=erlang.recordFormat=online_record
        string tbscore = File.ReadAllText(Path.Combine(ws.DataDir, "tbscore.erl"));
        Assert.Contains("get({1, 1}) -> #tbscore{uid = 1, stage = 1, score = 100};", tbscore);
        // TbGlobal 的 tags=erlang.recordFormat=online_tuple 优先于 xargs
        string tbglobal = File.ReadAllText(Path.Combine(ws.DataDir, "tbglobal.erl"));
        Assert.Contains("get() -> {tbglobal, 3, <<\"demo\"/utf8>>}.", tbglobal);
    }

    [Fact]
    public void Erlang_AbstractTable_FormattedRecord_NestedBeanMultiline()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("erlang"),
            dataTargets: new[] { "erlang" },
            outputDataDir: ws.DataDir);

        string src = File.ReadAllText(Path.Combine(ws.DataDir, "tbabstract.erl"));
        // 嵌套具体 bean 递归多行、逐层缩进 4 空格；容器（map）内的 record 保持单行
        Assert.Contains(
            "get(1) -> #normal{\n" +
            "    id = 1,\n" +
            "    num = 10,\n" +
            "    shape = #{'__type__' => circle, id => 5, time => #range{startTime = 100, endTime = 200}},\n" +
            "    extra = #range{\n" +
            "        startTime = 300,\n" +
            "        endTime = 400\n" +
            "    }\n" +
            "};", src);
        Assert.Contains("get(_) -> none.", src);
    }

    [Fact]
    public void Erlang_AbstractTable_OnlineTuple_RecursiveConversion()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("erlang"),
            dataTargets: new[] { "erlang" },
            outputDataDir: ws.DataDir,
            extraOptions: new Dictionary<string, string> { ["erlang.recordFormat"] = "online_tuple" });

        string src = File.ReadAllText(Path.Combine(ws.DataDir, "tbabstract.erl"));
        // 嵌套具体 bean 递归转换为 tuple；抽象 bean 字段仍为带 __type__ 的 map
        Assert.Contains(
            "get(1) -> {normal, 1, 10, #{'__type__' => circle, id => 5, time => {range, 100, 200}}, {range, 300, 400}};",
            src);
        Assert.Contains("get(2) -> {equipment, 2, 99};", src);
        Assert.Contains("get(_) -> none.", src);
    }

    [Fact]
    public void Erlang_RecordFormat_InvalidOptionValue_Fails()
    {
        var result = LubanRunner.GenerateToTemp(
            "core",
            dataTargets: new[] { "erlang" },
            extraOptions: new Dictionary<string, string> { ["erlang.recordFormat"] = "bogus" },
            throwOnError: false);
        Assert.False(result.Succeeded);
        Assert.Contains("formatted_record", result.Exception.ToString());
    }

    [Fact]
    public void Erlang_RecordFormats_Parse()
    {
        Assert.Equal(ErlangRecordFormat.FormattedRecord, ErlangRecordFormats.Parse("formatted_record", "test"));
        Assert.Equal(ErlangRecordFormat.OnlineRecord, ErlangRecordFormats.Parse("online_record", "test"));
        Assert.Equal(ErlangRecordFormat.OnlineTuple, ErlangRecordFormats.Parse("online_tuple", "test"));
        var e = Assert.Throws<LubanException>(() => ErlangRecordFormats.Parse("bogus", "test"));
        Assert.Contains("formatted_record", e.Message);
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
