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
    public void Erlang_Split_GeneratesFacadeAndShards()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("erlang-split"),
            codeTargets: new[] { "erlang-record" },
            dataTargets: new[] { "erlang" },
            outputCodeDir: ws.CodeDir,
            outputDataDir: ws.DataDir);

        // map 表 erlang.split=3：1 个门面加 3 个子模块
        Assert.Equal(new[] { "tbsplititem.erl", "tbsplititem_0.erl", "tbsplititem_1.erl", "tbsplititem_2.erl" },
            Directory.GetFiles(ws.DataDir, "tbsplititem*.erl").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal));
        // list 表 erlang.split=2：1 个门面加 2 个子模块
        Assert.Equal(new[] { "tbsplitscore.erl", "tbsplitscore_0.erl", "tbsplitscore_1.erl" },
            Directory.GetFiles(ws.DataDir, "tbsplitscore*.erl").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal));
        // 空分片照常生成
        Assert.Equal(new[] { "tbsplitsparse.erl", "tbsplitsparse_0.erl", "tbsplitsparse_1.erl", "tbsplitsparse_2.erl" },
            Directory.GetFiles(ws.DataDir, "tbsplitsparse*.erl").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal));
        // .hrl 数量与内容不随切分变化
        Assert.Equal(new[] { "tbsplititem.hrl", "tbsplitscore.hrl", "tbsplitsparse.hrl" },
            Directory.GetFiles(ws.CodeDir, "*.hrl").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal));

        string facade = File.ReadAllText(Path.Combine(ws.DataDir, "tbsplititem.erl"));
        Assert.Contains("-module(tbsplititem).", facade);
        Assert.DoesNotContain("-include", facade);
        Assert.Contains("get(Key) when is_integer(Key) ->", facade);
        Assert.Contains("    case ((Key rem 3) + 3) rem 3 of", facade);
        Assert.Contains("        0 -> tbsplititem_0:get(Key);", facade);
        Assert.Contains("        2 -> tbsplititem_2:get(Key)", facade);
        Assert.Contains("get(_) ->", facade);
        Assert.Contains("    none.", facade);
        Assert.Contains("    lists:append([tbsplititem_0:all(), tbsplititem_1:all(), tbsplititem_2:all()]).", facade);

        // 多字段索引取首字段取模
        string scoreFacade = File.ReadAllText(Path.Combine(ws.DataDir, "tbsplitscore.erl"));
        Assert.Contains("get(Key) when is_tuple(Key), is_integer(element(1, Key)) ->", scoreFacade);
        Assert.Contains("    case ((element(1, Key) rem 2) + 2) rem 2 of", scoreFacade);
        Assert.Contains("    lists:append([tbsplitscore_0:all(), tbsplitscore_1:all()]).", scoreFacade);

        string sub = File.ReadAllText(Path.Combine(ws.DataDir, "tbsplititem_0.erl"));
        Assert.Contains("-module(tbsplititem_0).", sub);
        Assert.Contains("-include(\"tbsplititem.hrl\").", sub);
        Assert.Contains("get(3) -> #tbsplititem{", sub);
        Assert.Contains("get(_) -> none.", sub);
        Assert.Contains("[3, 6].", sub);

        string emptySub = File.ReadAllText(Path.Combine(ws.DataDir, "tbsplitsparse_1.erl"));
        Assert.Contains("get(_) -> none.", emptySub);
        Assert.Contains("[]", emptySub);
    }

    [Fact]
    public void Erlang_Split_FacadeRoutesAtRuntime()
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
            TestPaths.FixtureConf("erlang-split"),
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
{tbsplititem, 1, <<"sword"/utf8>>} = tbsplititem:get(1),
{tbsplititem, 4, <<"wand"/utf8>>} = tbsplititem:get(4),
{tbsplititem, -2, <<"cursed"/utf8>>} = tbsplititem:get(-2),
{tbsplititem, 10, <<"crown"/utf8>>} = tbsplititem:get(10),
none = tbsplititem:get(99),
none = tbsplititem:get(atom_key),
[-2, 1, 2, 3, 4, 5, 6, 7, 10] = lists:sort(tbsplititem:all()),
{tbsplititem, 3, <<"bow"/utf8>>} = tbsplititem_0:get(3),
none = tbsplititem_0:get(1),
[3, 6] = tbsplititem_0:all(),
[-2, 1, 4, 7, 10] = lists:sort(tbsplititem_1:all()),
[2, 5] = lists:sort(tbsplititem_2:all()),
{tbsplitscore, 1, 1, 100} = tbsplitscore:get({1, 1}),
{tbsplitscore, 2, 1, 300} = tbsplitscore:get({2, 1}),
{tbsplitscore, -3, 5, 500} = tbsplitscore:get({-3, 5}),
none = tbsplitscore:get({9, 9}),
none = tbsplitscore:get(not_a_tuple),
none = tbsplitscore:get({not_int, 1}),
[{-3, 5}, {1, 1}, {1, 2}, {2, 1}, {4, 1}] = lists:sort(tbsplitscore:all()),
none = tbsplitscore_0:get({1, 1}),
{tbsplitscore, 4, 1, 400} = tbsplitscore_0:get({4, 1}),
[{2, 1}, {4, 1}] = lists:sort(tbsplitscore_0:all()),
{tbsplitsparse, 3, 30} = tbsplitsparse:get(3),
none = tbsplitsparse:get(4),
[] = tbsplitsparse_1:all(),
[] = tbsplitsparse_2:all(),
none = tbsplitsparse_2:get(6),
io:format("erlang split check OK~n"),
halt(0).
""";

        var runResult = Run(erl, new[]
        {
            "-noshell",
            "-pa", ebinDir,
            "-eval", evalCode,
        });
        Assert.True(runResult.ExitCode == 0 && runResult.Stdout.Contains("erlang split check OK"),
            $"erl runtime check failed:\n{runResult.Stdout}\n{runResult.Stderr}");
    }

    [Theory]
    [InlineData("erlang-split/bad-count", "erlang.split", "must be an integer >= 2")]
    [InlineData("erlang-split/one-mode", "does not support erlang.split", "mode 'one'")]
    [InlineData("erlang-split/indexless-list", "does not support erlang.split", "mode 'list'")]
    [InlineData("erlang-split/string-key", "does not support erlang.split", "mod field")]
    public void Erlang_Split_InvalidConfig_Fails(string fixture, string fragment1, string fragment2)
    {
        var result = LubanRunner.GenerateToTemp(
            fixture,
            dataTargets: new[] { "erlang" },
            throwOnError: false);
        Assert.False(result.Succeeded);
        Assert.Contains(fragment1, result.Exception.ToString());
        Assert.Contains(fragment2, result.Exception.ToString());
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
