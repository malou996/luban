// Copyright 2025 Code Philosophy
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Collections.Concurrent;
using Luban.CodeFormat;
using Luban.CodeTarget;
using Luban.Defs;
using Luban.Diagnostics;
using Luban.Schema;

namespace Luban.Erlang;

/// <summary>
/// erlang 导出的统一命名模块：.hrl 文件名、.erl 模块名、record 名、字段名共用同一推导。
/// 风格由 erlang.codeStyle / namingConvention.erlang.* 控制（缺省 erlang-default，全部 lower_snake）；
/// 模块名是否包含由 erlang.includeModule 控制（table tags 优先于 xargs 全局选项，默认不包含）；
/// table 显式 output 属性原样优先，不做转换也不受 includeModule 影响。
/// 每个 target 实例持有一个本类实例，实例内登记全部导出名并检测同名不同源冲突。
/// </summary>
public sealed class ErlangNaming
{
    public const string IncludeModuleTag = "erlang.includeModule";

    private const string IncludeModuleOption = "includeModule";
    private const string StyleFamily = "erlang";

    private readonly ICodeStyle _style;

    // record 名 → 定义来源 FullName；同一 bean 在多个 .hrl 中同名同定义属于设计内行为，同名不同源才冲突
    private readonly ConcurrentDictionary<string, string> _recordNameOwners = new();

    // erlang 模块名（含 split 门面与子模块）→ 来源表 FullName
    private readonly ConcurrentDictionary<string, string> _moduleNameOwners = new();

    public static ErlangNaming Create()
    {
        return new ErlangNaming(CodeTargetBase.CreateStyleFromOptions(StyleFamily, CodeFormatManager.Ins.ErlangDefaultCodeStyle));
    }

    internal ErlangNaming(ICodeStyle style)
    {
        _style = style;
    }

    public ErlangTableNaming ForTable(DefTable table)
    {
        return new ErlangTableNaming(this, table);
    }

    internal string FormatTypeName(string name)
    {
        return _style.FormatType(name);
    }

    internal string FormatFieldName(string name)
    {
        return _style.FormatField(name);
    }

    /// <summary>模块名各段与类型名各自转换后用 '_' 连接；模块名为空时只剩类型名。</summary>
    internal string JoinWithModule(string namespaceName, string typeName)
    {
        var segments = namespaceName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(_style.FormatNamespace)
            .ToList();
        segments.Add(typeName);
        return string.Join("_", segments);
    }

    /// <summary>解析表的 includeModule 取值：tags 优先，其次 erlang.includeModule 全局选项，默认 false。</summary>
    internal bool IncludeModule(DefTable table)
    {
        string tagValue = table.GetTag(IncludeModuleTag);
        if (tagValue != null)
        {
            return ParseBool(tagValue, $"table {table.FullName} tags");
        }
        string optionValue = EnvManager.Current.GetOptionOrDefault(StyleFamily, IncludeModuleOption, true, null);
        if (optionValue != null)
        {
            return ParseBool(optionValue, $"option {IncludeModuleTag}");
        }
        return false;
    }

    private static bool ParseBool(string value, string context)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "1":
            case "true":
                return true;
            case "0":
            case "false":
                return false;
            default:
                throw new LubanException("error.erlang.invalid_include_module", context, value);
        }
    }

    /// <summary>登记模块名；同名不同源（另一张表已占用）时报错。</summary>
    internal void CheckModuleName(DefTable table, string moduleName)
    {
        if (!_moduleNameOwners.TryAdd(moduleName, table.FullName) && _moduleNameOwners[moduleName] != table.FullName)
        {
            throw new LubanException(table.Source, "error.erlang.module_name_conflict", moduleName, _moduleNameOwners[moduleName], table.FullName);
        }
    }

    /// <summary>登记 record 名；同名不同源（另一个 bean 或表已占用）时报错。</summary>
    internal void CheckRecordName(SchemaSource source, string recordName, string ownerFullName)
    {
        if (!_recordNameOwners.TryAdd(recordName, ownerFullName) && _recordNameOwners[recordName] != ownerFullName)
        {
            throw new LubanException(source, "error.erlang.record_name_conflict", recordName, _recordNameOwners[recordName], ownerFullName);
        }
    }
}

/// <summary>
/// 单张表的命名视图：模块名（.hrl 文件名、.erl 模块名、表 record 名共用）、bean record 名、字段名。
/// includeModule 取本表配置，因此同一 bean 在不同 includeModule 设置的表中 record 名可以不同。
/// </summary>
public sealed class ErlangTableNaming
{
    private readonly ErlangNaming _naming;
    private string _moduleName;

    internal ErlangTableNaming(ErlangNaming naming, DefTable table)
    {
        _naming = naming;
        Table = table;
    }

    public DefTable Table { get; }

    /// <summary>表模块名：显式 output 原样优先；否则类型名经 style 转换，includeModule 时带模块名前缀。</summary>
    public string ModuleName => _moduleName ??= ComputeModuleName();

    /// <summary>bean 的 record 名：类型名经 style 转换，includeModule 时带 bean 自身模块名前缀。</summary>
    public string RecordName(DefBean bean)
    {
        string typeName = _naming.FormatTypeName(bean.Name);
        return _naming.IncludeModule(Table) ? _naming.JoinWithModule(bean.Namespace, typeName) : typeName;
    }

    /// <summary>字段名经 style 的 field 维度转换。</summary>
    public string FieldName(string name)
    {
        return _naming.FormatFieldName(name);
    }

    internal ErlangNaming Naming => _naming;

    private string ComputeModuleName()
    {
        if (Table.ExplicitOutputFile != null)
        {
            return Table.ExplicitOutputFile;
        }
        string typeName = _naming.FormatTypeName(Table.Name);
        return _naming.IncludeModule(Table) ? _naming.JoinWithModule(Table.Namespace, typeName) : typeName;
    }
}
