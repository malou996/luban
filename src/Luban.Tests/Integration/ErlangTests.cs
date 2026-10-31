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
Item = tb_item:get(1),
{tb_item, 1, <<"sword"/utf8>>, 1, _, _, _, #{'__type__' := circle, id := 10, radius := 1.5}} = Item,
none = tb_item:get(99),
[1, 2] = lists:sort(tb_item:all()),
{tb_global, 3, <<"demo"/utf8>>} = tb_global:get(),
{tb_score, 1, 1, 100} = tb_score:get({1, 1}),
none = tb_score:get({9, 9}),
[{1, 1}, {1, 2}, {2, 1}] = lists:sort(tb_score:all()),
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
{normal, 1, 10, #{'__type__' := circle, id := 5, time := {range, 100, 200}}, {range, 300, 400}} = tb_abstract:get(1),
{equipment, 2, 99} = tb_abstract:get(2),
none = tb_abstract:get(3),
[1, 2] = lists:sort(tb_abstract:all()),
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

        string tb_item = File.ReadAllText(Path.Combine(ws.DataDir, "tb_item.erl"));
        // 默认 formatted_record：record 直接跟在 -> 后，每个字段独占一行、4 空格缩进，'}' 回到子句起始缩进
        Assert.Contains(
            "get(1) -> #tb_item{\n" +
            "    id = 1,\n" +
            "    name = <<\"sword\"/utf8>>,\n" +
            "    quality = 1,\n", tb_item);
        // 抽象 bean 字段保持带 __type__ 的单行 map
        Assert.Contains("shape = #{'__type__' => circle, id => 10, radius => 1.5}\n};", tb_item);
        Assert.Contains("get(_) -> none.", tb_item);
    }

    [Fact]
    public void Core_Erlang_RecordFormat_TagTakesPriorityOverXargsFallback()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("core"),
            dataTargets: new[] { "erlang" },
            outputDataDir: ws.DataDir,
            extraOptions: new Dictionary<string, string> { ["erlang.recordFormat"] = "compact_record" });

        // TbItem 未配置 tags，回退到 xargs 的 compact_record
        string tb_item = File.ReadAllText(Path.Combine(ws.DataDir, "tb_item.erl"));
        Assert.Contains("get(1) -> #tb_item{id = 1, name = <<\"sword\"/utf8>>, quality = 1,", tb_item);
        // TbScore 的 tags=erlang.recordFormat=compact_record
        string tb_score = File.ReadAllText(Path.Combine(ws.DataDir, "tb_score.erl"));
        Assert.Contains("get({1, 1}) -> #tb_score{uid = 1, stage = 1, score = 100};", tb_score);
        // TbGlobal 的 tags=erlang.recordFormat=compact_tuple 优先于 xargs
        string tb_global = File.ReadAllText(Path.Combine(ws.DataDir, "tb_global.erl"));
        Assert.Contains("get() -> {tb_global, 3, <<\"demo\"/utf8>>}.", tb_global);
    }

    [Fact]
    public void Erlang_AbstractTable_FormattedRecord_NestedBeanMultiline()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("erlang"),
            dataTargets: new[] { "erlang" },
            outputDataDir: ws.DataDir);

        string src = File.ReadAllText(Path.Combine(ws.DataDir, "tb_abstract.erl"));
        // 嵌套具体 bean 递归多行、逐层缩进 4 空格；容器（map）内的 record 保持单行
        Assert.Contains(
            "get(1) -> #normal{\n" +
            "    id = 1,\n" +
            "    num = 10,\n" +
            "    shape = #{'__type__' => circle, id => 5, time => #range{start_time = 100, end_time = 200}},\n" +
            "    extra = #range{\n" +
            "        start_time = 300,\n" +
            "        end_time = 400\n" +
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
            extraOptions: new Dictionary<string, string> { ["erlang.recordFormat"] = "compact_tuple" });

        string src = File.ReadAllText(Path.Combine(ws.DataDir, "tb_abstract.erl"));
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
        Assert.Equal(new[] { "tb_split_item.erl", "tb_split_item_0.erl", "tb_split_item_1.erl", "tb_split_item_2.erl" },
            Directory.GetFiles(ws.DataDir, "tb_split_item*.erl").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal));
        // list 表 erlang.split=2：1 个门面加 2 个子模块
        Assert.Equal(new[] { "tb_split_score.erl", "tb_split_score_0.erl", "tb_split_score_1.erl" },
            Directory.GetFiles(ws.DataDir, "tb_split_score*.erl").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal));
        // 空分片照常生成
        Assert.Equal(new[] { "tb_split_sparse.erl", "tb_split_sparse_0.erl", "tb_split_sparse_1.erl", "tb_split_sparse_2.erl" },
            Directory.GetFiles(ws.DataDir, "tb_split_sparse*.erl").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal));
        // .hrl 数量与内容不随切分变化
        Assert.Equal(new[] { "tb_split_item.hrl", "tb_split_score.hrl", "tb_split_sparse.hrl" },
            Directory.GetFiles(ws.CodeDir, "*.hrl").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal));

        string facade = File.ReadAllText(Path.Combine(ws.DataDir, "tb_split_item.erl"));
        Assert.Contains("-module(tb_split_item).", facade);
        Assert.DoesNotContain("-include", facade);
        Assert.Contains("get(Key) when is_integer(Key) ->", facade);
        Assert.Contains("    case ((Key rem 3) + 3) rem 3 of", facade);
        Assert.Contains("        0 -> tb_split_item_0:get(Key);", facade);
        Assert.Contains("        2 -> tb_split_item_2:get(Key)", facade);
        Assert.Contains("get(_) ->", facade);
        Assert.Contains("    none.", facade);
        Assert.Contains("    lists:append([tb_split_item_0:all(), tb_split_item_1:all(), tb_split_item_2:all()]).", facade);

        // 多字段索引取首字段取模
        string scoreFacade = File.ReadAllText(Path.Combine(ws.DataDir, "tb_split_score.erl"));
        Assert.Contains("get(Key) when is_tuple(Key), is_integer(element(1, Key)) ->", scoreFacade);
        Assert.Contains("    case ((element(1, Key) rem 2) + 2) rem 2 of", scoreFacade);
        Assert.Contains("    lists:append([tb_split_score_0:all(), tb_split_score_1:all()]).", scoreFacade);

        string sub = File.ReadAllText(Path.Combine(ws.DataDir, "tb_split_item_0.erl"));
        Assert.Contains("-module(tb_split_item_0).", sub);
        Assert.Contains("-include(\"tb_split_item.hrl\").", sub);
        Assert.Contains("get(3) -> #tb_split_item{", sub);
        Assert.Contains("get(_) -> none.", sub);
        Assert.Contains("[3, 6].", sub);

        string emptySub = File.ReadAllText(Path.Combine(ws.DataDir, "tb_split_sparse_1.erl"));
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
{tb_split_item, 1, <<"sword"/utf8>>} = tb_split_item:get(1),
{tb_split_item, 4, <<"wand"/utf8>>} = tb_split_item:get(4),
{tb_split_item, -2, <<"cursed"/utf8>>} = tb_split_item:get(-2),
{tb_split_item, 10, <<"crown"/utf8>>} = tb_split_item:get(10),
none = tb_split_item:get(99),
none = tb_split_item:get(atom_key),
[-2, 1, 2, 3, 4, 5, 6, 7, 10] = lists:sort(tb_split_item:all()),
{tb_split_item, 3, <<"bow"/utf8>>} = tb_split_item_0:get(3),
none = tb_split_item_0:get(1),
[3, 6] = tb_split_item_0:all(),
[-2, 1, 4, 7, 10] = lists:sort(tb_split_item_1:all()),
[2, 5] = lists:sort(tb_split_item_2:all()),
{tb_split_score, 1, 1, 100} = tb_split_score:get({1, 1}),
{tb_split_score, 2, 1, 300} = tb_split_score:get({2, 1}),
{tb_split_score, -3, 5, 500} = tb_split_score:get({-3, 5}),
none = tb_split_score:get({9, 9}),
none = tb_split_score:get(not_a_tuple),
none = tb_split_score:get({not_int, 1}),
[{-3, 5}, {1, 1}, {1, 2}, {2, 1}, {4, 1}] = lists:sort(tb_split_score:all()),
none = tb_split_score_0:get({1, 1}),
{tb_split_score, 4, 1, 400} = tb_split_score_0:get({4, 1}),
[{2, 1}, {4, 1}] = lists:sort(tb_split_score_0:all()),
{tb_split_sparse, 3, 30} = tb_split_sparse:get(3),
none = tb_split_sparse:get(4),
[] = tb_split_sparse_1:all(),
[] = tb_split_sparse_2:all(),
none = tb_split_sparse_2:get(6),
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
    public void Erlang_AtomTag_RendersStringFieldsAsAtom()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("erlang-atom"),
            codeTargets: new[] { "erlang-record" },
            dataTargets: new[] { "erlang" },
            outputCodeDir: ws.CodeDir,
            outputDataDir: ws.DataDir);

        // 带 erlang.atom tag 的 string：record 字段、list 元素、map 键、索引键均按 atom 渲染；无 tag 字段保持 utf8 binary
        string src = File.ReadAllText(Path.Combine(ws.DataDir, "tb_atom.erl"));
        Assert.Contains(
            "get(fire) -> #tb_atom{key = fire, plain = <<\"火焰\"/utf8>>, skills = [fire_ball, heal], stats = #{hp => 100, mp => 50}};",
            src);
        // 非小写字母开头的值输出带引号 atom
        Assert.Contains(
            "get('Ice-Master') -> #tb_atom{key = 'Ice-Master', plain = <<\"plain text\"/utf8>>, skills = [frost], stats = #{atk => 9}};",
            src);
        Assert.Contains("get(_) -> none.", src);
        Assert.Contains("[fire, 'Ice-Master'].", src);

        // record 头类型注解：tag 字段 atom()，无 tag 字段 binary()
        string hrl = File.ReadAllText(Path.Combine(ws.CodeDir, "tb_atom.hrl"));
        Assert.Contains("key :: atom(),", hrl);
        Assert.Contains("plain :: binary(),", hrl);
        Assert.Contains("skills :: [atom()],", hrl);
        Assert.Contains("stats :: #{atom() := integer()}", hrl);
    }

    [Fact]
    public void Erlang_AtomTag_ErlcCompileAndLoad()
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
            TestPaths.FixtureConf("erlang-atom"),
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

        // eval 参数经命令行传递，只使用 ASCII；火焰行的 binary 内容由源码断言覆盖
        const string evalCode = """
{tb_atom, fire, Plain, [fire_ball, heal], #{hp := 100, mp := 50}} = tb_atom:get(fire),
true = is_binary(Plain),
{tb_atom, 'Ice-Master', <<"plain text"/utf8>>, [frost], #{atk := 9}} = tb_atom:get('Ice-Master'),
none = tb_atom:get(unknown),
[fire, 'Ice-Master'] = tb_atom:all(),
io:format("erlang atom check OK~n"),
halt(0).
""";

        var runResult = Run(erl, new[]
        {
            "-noshell",
            "-pa", ebinDir,
            "-eval", evalCode,
        });
        Assert.True(runResult.ExitCode == 0 && runResult.Stdout.Contains("erlang atom check OK"),
            $"erl runtime check failed:\n{runResult.Stdout}\n{runResult.Stderr}");
    }

    [Fact]
    public void Erlang_Macro_GeneratesHrl_MatchesGolden()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("erlang-macro"),
            dataTargets: new[] { "erlang-macro" },
            outputDataDir: ws.DataDir);
        GoldenAssert.AssertDirectoryMatches(ws.DataDir, "erlang-macro/erlang-macro");
    }

    [Fact]
    public void Erlang_Macro_OnlyTaggedTablesProduceFiles()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("erlang-macro"),
            dataTargets: new[] { "erlang-macro" },
            outputDataDir: ws.DataDir);

        // 未配置 tag 的 TbPlain 不产出文件；文件名来自 output 参数或表模块名（缺省）
        Assert.Equal(new[] { "error_code.hrl", "tb_atom.hrl", "tb_constant.hrl", "tb_empty.hrl", "tb_flag.hrl", "tb_text.hrl" },
            Directory.GetFiles(ws.DataDir, "*.hrl").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal));

        // 注释纵向排在最长 define 行之后 2 个空格处
        string errorCode = File.ReadAllText(Path.Combine(ws.DataDir, "error_code.hrl"));
        Assert.Contains("-ifndef(LUBAN_ERROR_CODE_HRL).", errorCode);
        Assert.Contains("-define(E_LOGIN_BAN_ROLE, 7).  %% 该角色被禁止登陆", errorCode);
        Assert.Contains("-define(E_OK, 0).".PadRight("-define(E_LOGIN_BAN_ROLE, 7).".Length + 2) + "%% 成功", errorCode);
        Assert.Equal("-define(E_SILENT, 8).",
            File.ReadAllLines(Path.Combine(ws.DataDir, "error_code.hrl")).Single(l => l.StartsWith("-define(E_SILENT")));
        Assert.Contains("-endif. %% LUBAN_ERROR_CODE_HRL", errorCode);

        // float 渲染；缺省文件名 = 表模块名
        string constant = File.ReadAllText(Path.Combine(ws.DataDir, "tb_constant.hrl"));
        Assert.Contains("-ifndef(LUBAN_TB_CONSTANT_HRL).", constant);
        Assert.Contains("-define(RATIO_HALF, 0.5).", constant);
        Assert.Contains("-define(RATIO_PI, 3.14159).", constant);

        // string 值渲染为 utf8 binary；注释为空的行不带 %%
        string text = File.ReadAllText(Path.Combine(ws.DataDir, "tb_text.hrl"));
        Assert.Contains("-define(MSG_HELLO, <<\"hello\"/utf8>>).", text);
        Assert.Contains("%% 欢迎语", text);
        Assert.Equal("-define(MSG_QUOTE, <<\"say \\\"hi\\\"\"/utf8>>).",
            File.ReadAllLines(Path.Combine(ws.DataDir, "tb_text.hrl")).Single(l => l.StartsWith("-define(MSG_QUOTE")));

        // :atom 提示渲染为 atom
        string atom = File.ReadAllText(Path.Combine(ws.DataDir, "tb_atom.hrl"));
        Assert.Contains("-define(KIND_BADARG, badarg).", atom);
        Assert.Contains("-define(KIND_ERR, error).", atom);

        // '=' 分隔符与 bool 渲染
        string flag = File.ReadAllText(Path.Combine(ws.DataDir, "tb_flag.hrl"));
        Assert.Contains("-define(FLAG_ON, true).", flag);
        Assert.Contains("-define(FLAG_OFF, false).", flag);

        // 空表：只含 guard 的文件
        string empty = File.ReadAllText(Path.Combine(ws.DataDir, "tb_empty.hrl"));
        Assert.Contains("-ifndef(LUBAN_TB_EMPTY_HRL).", empty);
        Assert.DoesNotContain("-define(E", empty);
        Assert.Contains("-endif. %% LUBAN_TB_EMPTY_HRL", empty);
    }

    [Fact]
    public void Erlang_Macro_ErlangDataTargetIgnoresMacroTag()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("erlang-macro"),
            dataTargets: new[] { "erlang" },
            outputDataDir: ws.DataDir);
        Assert.Empty(Directory.GetFiles(ws.DataDir, "*.hrl"));
        // 含未配置 tag 的表在内，每张表各一个数据模块
        Assert.Equal(7, Directory.GetFiles(ws.DataDir, "*.erl").Length);
    }

    [Fact]
    public void Erlang_Macro_ErlcCompileAndIncludeCheck()
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
            TestPaths.FixtureConf("erlang-macro"),
            dataTargets: new[] { "erlang-macro" },
            outputDataDir: ws.DataDir);

        string ebinDir = Path.Combine(ws.Root, "ebin");
        Directory.CreateDirectory(ebinDir);
        File.WriteAllText(Path.Combine(ws.Root, "macro_check.erl"), """
-module(macro_check).
-export([check/0]).
-include("error_code.hrl").
-include("tb_constant.hrl").
-include("tb_text.hrl").
-include("tb_atom.hrl").
-include("tb_flag.hrl").
check() ->
    0 = ?E_OK,
    7 = ?E_LOGIN_BAN_ROLE,
    0.5 = ?RATIO_HALF,
    <<"hello"/utf8>> = ?MSG_HELLO,
    badarg = ?KIND_BADARG,
    true = ?FLAG_ON,
    false = ?FLAG_OFF,
    ok.
""");

        var compile = Run(erlc, new[] { "-Werror", "-I", ws.DataDir, "-o", ebinDir, Path.Combine(ws.Root, "macro_check.erl") });
        Assert.True(compile.ExitCode == 0, $"erlc failed:\n{compile.Stdout}\n{compile.Stderr}");

        const string evalCode = """
ok = macro_check:check(),
io:format("erlang macro check OK~n"),
halt(0).
""";

        var runResult = Run(erl, new[] { "-noshell", "-pa", ebinDir, "-eval", evalCode });
        Assert.True(runResult.ExitCode == 0 && runResult.Stdout.Contains("erlang macro check OK"),
            $"erl runtime check failed:\n{runResult.Stdout}\n{runResult.Stderr}");
    }

    [Theory]
    [InlineData("erlang-macro/bad-macro-name", "is not a valid macro name", "'1BAD'")]
    [InlineData("erlang-macro/bad-duplicate-name", "macro name 'E_OK' is duplicated", "record2 from")]
    [InlineData("erlang-macro/bad-output-conflict", "erlang macro file 'shared_name.hrl' conflicts", "adjust the output parameter")]
    [InlineData("erlang-macro/bad-value-type", "is not supported", "supported types: bool, byte, short, int, long, float, double, string")]
    public void Erlang_Macro_InvalidConfig_Fails(string fixture, string fragment1, string fragment2)
    {
        var result = LubanRunner.GenerateToTemp(
            fixture,
            dataTargets: new[] { "erlang-macro" },
            throwOnError: false);
        Assert.False(result.Succeeded);
        Assert.Contains(fragment1, result.Exception.ToString());
        Assert.Contains(fragment2, result.Exception.ToString());
    }

    [Fact]
    public void Erlang_MacroTagParams_Parse()
    {
        var full = ErlangMacroTagParams.Parse("name:code,value:id,comment:desc,output:error_code", "test context");
        Assert.Equal("code", full.NameField);
        Assert.Equal("id", full.ValueField);
        Assert.False(full.ValueAsAtom);
        Assert.Equal("desc", full.CommentField);
        Assert.Equal("error_code", full.OutputFile);

        // value 第三段 :atom 提示；comment/output 缺省为 null
        var atom = ErlangMacroTagParams.Parse("name:name,value:val:atom", "test context");
        Assert.Equal("val", atom.ValueField);
        Assert.True(atom.ValueAsAtom);
        Assert.Null(atom.CommentField);
        Assert.Null(atom.OutputFile);

        // '=' 与 ':' 分隔符混用
        var eq = ErlangMacroTagParams.Parse("name=name,value=on", "test context");
        Assert.Equal("name", eq.NameField);
        Assert.Equal("on", eq.ValueField);

        AssertParamError("value:id", "missing required parameter 'name'");
        AssertParamError("name:code", "missing required parameter 'value'");
        AssertParamError("erlang.macro", "missing required parameter 'name'");
        AssertParamError("name:code,value:id,bogus:1", "unknown parameter 'bogus'");
        AssertParamError("name:code,name:desc,value:id", "duplicate parameter 'name'");
        AssertParamError("name:code,", "empty parameter");
        AssertParamError("name:,value:id", "empty parameter");
        AssertParamError("name:code,value:id:color", "only ':atom' is supported");
        AssertParamError("name:code,value:id,output:a/b", "invalid erlang.macro output file name 'a/b'");
    }

    private static void AssertParamError(string tagValue, string fragment)
    {
        var e = Assert.Throws<LubanException>(() => ErlangMacroTagParams.Parse(tagValue, "test context"));
        Assert.Contains(fragment, e.Message);
    }

    [Fact]
    public void Erlang_RecordFormats_Parse()
    {
        Assert.Equal(ErlangRecordFormat.FormattedRecord, ErlangRecordFormats.Parse("formatted_record", "test"));
        Assert.Equal(ErlangRecordFormat.OnlineRecord, ErlangRecordFormats.Parse("compact_record", "test"));
        Assert.Equal(ErlangRecordFormat.OnlineTuple, ErlangRecordFormats.Parse("compact_tuple", "test"));
        var e = Assert.Throws<LubanException>(() => ErlangRecordFormats.Parse("bogus", "test"));
        Assert.Contains("formatted_record", e.Message);
    }

    [Fact]
    public void Erlang_Naming_MatchesGolden()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("erlang-naming"),
            codeTargets: new[] { "erlang-record" },
            dataTargets: new[] { "erlang" },
            outputCodeDir: ws.CodeDir,
            outputDataDir: ws.DataDir);
        GoldenAssert.AssertDirectoryMatches(ws.CodeDir, "erlang-naming/erlang-record");
        GoldenAssert.AssertDirectoryMatches(ws.DataDir, "erlang-naming/erlang");
    }

    [Fact]
    public void Erlang_Naming_DefaultIsLowerSnakeWithoutModule()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("erlang-naming"),
            codeTargets: new[] { "erlang-record" },
            dataTargets: new[] { "erlang" },
            outputCodeDir: ws.CodeDir,
            outputDataDir: ws.DataDir);

        // 默认不含模块名：表名 lower_snake 转换
        Assert.Equal(new[] { "cfg_item_tb_named.erl", "my_table.erl", "tb_reward.erl" },
            Directory.GetFiles(ws.DataDir, "*.erl").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal));
        Assert.Equal(new[] { "cfg_item_tb_named.hrl", "my_table.hrl", "tb_reward.hrl" },
            Directory.GetFiles(ws.CodeDir, "*.hrl").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal));

        string rewardHrl = File.ReadAllText(Path.Combine(ws.CodeDir, "tb_reward.hrl"));
        Assert.Contains("-record(bonus, {", rewardHrl);
        Assert.Contains("-record(tb_reward, {", rewardHrl);
        Assert.Contains("item_id :: integer()", rewardHrl);

        // tags 开启 includeModule：模块名各段与类型名各自转换后连接
        string namedHrl = File.ReadAllText(Path.Combine(ws.CodeDir, "cfg_item_tb_named.hrl"));
        Assert.Contains("-record(cfg_item_tb_named, {", namedHrl);
        // 值 bean 本身即表 record；字段名 startTime→start_time、ItemID→item_id
        Assert.Contains("start_time :: integer()", namedHrl);
        Assert.Contains("item_id :: integer()", namedHrl);

        // 显式 output 原样优先，不受 includeModule 与转换影响；共享 bean 的 record 名不带模块名
        string customHrl = File.ReadAllText(Path.Combine(ws.CodeDir, "my_table.hrl"));
        Assert.Contains("-record(my_table, {", customHrl);
        Assert.Contains("-record(bonus, {", customHrl);
        string customErl = File.ReadAllText(Path.Combine(ws.DataDir, "my_table.erl"));
        Assert.Contains("-module(my_table).", customErl);

        string namedErl = File.ReadAllText(Path.Combine(ws.DataDir, "cfg_item_tb_named.erl"));
        Assert.Contains("-module(cfg_item_tb_named).", namedErl);
        Assert.Contains("-include(\"cfg_item_tb_named.hrl\").", namedErl);
        Assert.Contains("item_id = 5", namedErl);
    }

    [Fact]
    public void Erlang_Naming_GlobalIncludeModuleOptionPrefixesAllTables()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("erlang-naming"),
            codeTargets: new[] { "erlang-record" },
            dataTargets: new[] { "erlang" },
            outputCodeDir: ws.CodeDir,
            outputDataDir: ws.DataDir,
            extraOptions: new Dictionary<string, string> { ["erlang.includeModule"] = "true" });

        Assert.Equal(new[] { "cfg_item_tb_named.erl", "cfg_item_tb_reward.erl", "my_table.erl" },
            Directory.GetFiles(ws.DataDir, "*.erl").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal));

        // bean record 名同样带模块名前缀；显式 output 的表不受影响
        string rewardHrl = File.ReadAllText(Path.Combine(ws.CodeDir, "cfg_item_tb_reward.hrl"));
        Assert.Contains("-record(cfg_item_bonus, {", rewardHrl);
        Assert.Contains("-record(cfg_item_tb_reward, {", rewardHrl);
        Assert.True(File.Exists(Path.Combine(ws.CodeDir, "my_table.hrl")));
    }

    [Fact]
    public void Erlang_Naming_CodeStyleNoneKeepsSchemaRawNames()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("erlang-naming"),
            codeTargets: new[] { "erlang-record" },
            dataTargets: new[] { "erlang" },
            outputCodeDir: ws.CodeDir,
            outputDataDir: ws.DataDir,
            extraOptions: new Dictionary<string, string> { ["erlang.codeStyle"] = "none" });

        Assert.Equal(new[] { "TbReward.erl", "cfg_item_TbNamed.erl", "my_table.erl" },
            Directory.GetFiles(ws.DataDir, "*.erl").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal));

        string rewardHrl = File.ReadAllText(Path.Combine(ws.CodeDir, "TbReward.hrl"));
        Assert.Contains("-record('TbReward', {", rewardHrl);
        Assert.Contains("-record('Bonus', {", rewardHrl);
        // 宏名按 erlang 惯例整体大写，codeStyle=none 下同样大写
        Assert.Contains("-ifndef(LUBAN_TBREWARD_HRL).", rewardHrl);
        string rewardErl = File.ReadAllText(Path.Combine(ws.DataDir, "TbReward.erl"));
        Assert.Contains("-module('TbReward').", rewardErl);

        // includeModule 独立于 codeStyle 仍然生效，模块名片段保持原样；小写开头的 atom 不加引号
        string namedHrl = File.ReadAllText(Path.Combine(ws.CodeDir, "cfg_item_TbNamed.hrl"));
        Assert.Contains("-record(cfg_item_TbNamed, {", namedHrl);
        Assert.Contains("startTime :: integer()", namedHrl);
        // 大写开头的字段名输出引号 atom
        Assert.Contains("'ItemID' :: integer()", namedHrl);
    }

    [Fact]
    public void Erlang_Naming_IncludeModuleResolvesRecordConflict()
    {
        using var ws = new TempWorkspace();
        LubanRunner.Run(
            TestPaths.FixtureConf("erlang-naming/record-conflict"),
            codeTargets: new[] { "erlang-record" },
            outputCodeDir: ws.CodeDir,
            extraOptions: new Dictionary<string, string> { ["erlang.includeModule"] = "true" });

        string mainHrl = File.ReadAllText(Path.Combine(ws.CodeDir, "tb_main.hrl"));
        Assert.Contains("-record(reward, {", mainHrl);
        string otherHrl = File.ReadAllText(Path.Combine(ws.CodeDir, "other_tb_other.hrl"));
        Assert.Contains("-record(other_reward, {", otherHrl);
    }

    [Fact]
    public void Erlang_Naming_ModuleNameConflict_Fails()
    {
        var result = LubanRunner.GenerateToTemp(
            "erlang-naming/module-conflict",
            dataTargets: new[] { "erlang" },
            throwOnError: false);
        Assert.False(result.Succeeded);
        Assert.Contains("erlang module name 'tb_right' conflicts", result.Exception.ToString());
        Assert.Contains("'TbLeft'", result.Exception.ToString());
        Assert.Contains("'TbRight'", result.Exception.ToString());
    }

    [Fact]
    public void Erlang_Naming_RecordNameConflict_Fails()
    {
        var result = LubanRunner.GenerateToTemp(
            "erlang-naming/record-conflict",
            codeTargets: new[] { "erlang-record" },
            throwOnError: false);
        Assert.False(result.Succeeded);
        Assert.Contains("erlang record name 'reward' conflicts", result.Exception.ToString());
        Assert.Contains("'other.Reward'", result.Exception.ToString());
    }

    [Fact]
    public void Erlang_Naming_InvalidIncludeModule_Fails()
    {
        var result = LubanRunner.GenerateToTemp(
            "erlang-naming/bad-include-module",
            dataTargets: new[] { "erlang" },
            throwOnError: false);
        Assert.False(result.Succeeded);
        Assert.Contains("must be true/false", result.Exception.ToString());
    }

    [Fact]
    public void Erlang_Naming_ErlcCompileAndLoad()
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
            TestPaths.FixtureConf("erlang-naming"),
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
{tb_reward, 1, 10, {bonus, 5}} = tb_reward:get(1),
{cfg_item_tb_named, 100, 5} = cfg_item_tb_named:get(5),
{my_table, 3, 30, {bonus, 7}} = my_table:get(3),
none = tb_reward:get(9),
[1, 2] = lists:sort(tb_reward:all()),
io:format("erlang naming check OK~n"),
halt(0).
""";

        var runResult = Run(erl, new[]
        {
            "-noshell",
            "-pa", ebinDir,
            "-eval", evalCode,
        });
        Assert.True(runResult.ExitCode == 0 && runResult.Stdout.Contains("erlang naming check OK"),
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
