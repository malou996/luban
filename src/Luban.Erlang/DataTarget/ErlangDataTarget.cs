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

using Luban.CodeTarget;
using Luban.Datas;
using Luban.DataTarget;
using Luban.Defs;
using Luban.Diagnostics;
using Luban.Erlang.DataVisitors;
using Luban.Types;
using System.Linq;
using System.Text;

namespace Luban.Erlang.DataTarget;

/// <summary>
/// 每张表导出 Erlang 源文件。
/// map 表：get/1 按主键返回 record，未匹配返回 none，all/0 返回全部主键；
/// list 表：有唯一索引时 get/1 按索引返回 record（多字段索引为 tuple），未匹配返回 none，all/0 返回全部索引；无索引时 all/0 返回全部记录；
/// 单例表：get/0 返回唯一记录。
/// 模块名、record 名、字段名由 ErlangNaming 统一推导（缺省 lower_snake，不带模块名），
/// 配置见 erlang.codeStyle、namingConvention.erlang.*、erlang.includeModule 与 table 的 output 属性；
/// 同名不同源的模块名在导出时报错。
/// 表定义 tags 上的 erlang.split=N（N 为 >= 2 的整数）将表切分为 N 个数据子模块（表名_0 .. 表名_N-1）
/// 加一个门面模块（表名）：门面按主键（或索引首字段）对 N 归一化取模路由 get/1，
/// all/0 聚合各子模块结果；仅支持 map 表与带索引的 list 表，取模字段仅支持整型家族与 enum。
/// record 值的渲染格式由 erlang.recordFormat 控制（formatted_record / compact_record / compact_tuple），
/// 表定义 tags 上的 erlang.recordFormat 优先于 xargs 选项，缺省 formatted_record。
/// record 定义在同名 .hrl 头文件中（由 erlang-record code target 生成），切分不改变 .hrl 的数量与内容。
/// </summary>
[DataTarget("erlang")]
public class ErlangDataTarget : DataTargetBase
{
    private const string RecordFormatTag = "erlang.recordFormat";
    private const string RecordFormatOption = "recordFormat";
    private const string SplitTag = "erlang.split";

    // 构造时立即创建：并行的表导出任务共享同一实例做重名检测；与 erlang-record code target 同源解析
    private readonly ErlangNaming _naming = ErlangNaming.Create();

    private ErlangNaming Naming => _naming;

    protected override string DefaultOutputFileExt => "erl";

    public override OutputFile ExportTable(DefTable table, List<Record> records)
    {
        var tableNaming = Naming.ForTable(table);
        string moduleName = tableNaming.ModuleName;
        Naming.CheckModuleName(table, moduleName);
        return CreateOutputFile($"{moduleName}.{OutputFileExt}", BuildDataModule(table, records, tableNaming, moduleName));
    }

    public override IReadOnlyList<OutputFile> ExportTableFiles(DefTable table, List<Record> records)
    {
        int? splitCount = ResolveSplitCount(table);
        if (splitCount == null)
        {
            return base.ExportTableFiles(table, records);
        }
        int n = splitCount.Value;
        var tableNaming = Naming.ForTable(table);
        string moduleName = tableNaming.ModuleName;

        var files = new List<OutputFile>(n + 1);
        Naming.CheckModuleName(table, moduleName);
        files.Add(CreateOutputFile($"{moduleName}.{OutputFileExt}", BuildFacadeModule(table, tableNaming, n)));
        List<Record>[] shards = PartitionRecords(table, records, n);
        for (int i = 0; i < n; i++)
        {
            string subModuleName = $"{moduleName}_{i}";
            Naming.CheckModuleName(table, subModuleName);
            files.Add(CreateOutputFile($"{subModuleName}.{OutputFileExt}", BuildDataModule(table, shards[i], tableNaming, subModuleName)));
        }
        return files;
    }

    private string BuildDataModule(DefTable table, List<Record> records, ErlangTableNaming tableNaming, string moduleName)
    {
        var valueBean = table.ValueTType.DefBean;

        // 抽象值表（多态行）没有统一的表 record，各记录按具体子类型的 record 名渲染
        string recordName = valueBean.IsAbstractType ? null : ErlangFormat.Atom(tableNaming.ModuleName);
        var format = ResolveRecordFormat(table);

        var sb = new StringBuilder();
        sb.Append(CommonFileHeaders.AUTO_GENERATE_ERLANG);
        sb.AppendLine($"-module({ErlangFormat.Atom(moduleName)}).");
        sb.AppendLine($"-include(\"{tableNaming.ModuleName}.hrl\").");
        sb.AppendLine();

        switch (table.Mode)
        {
            case TableMode.MAP:
            {
                sb.AppendLine("-export([get/1, all/0]).");
                sb.AppendLine();
                var visitor = new ToErlangLiteralVisitor(format, tableNaming);
                foreach (Record r in records)
                {
                    sb.AppendLine($"get({FormatIndexKey(r, table, visitor)}) -> {visitor.ApplyTopRecord(r.Data, recordName)};");
                }
                sb.AppendLine("get(_) -> none.");
                sb.AppendLine();
                AppendAllKeys(sb, records, table, format, tableNaming);
                break;
            }
            case TableMode.ONE:
            {
                sb.AppendLine("-export([get/0]).");
                sb.AppendLine();
                var visitor = new ToErlangLiteralVisitor(format, tableNaming);
                sb.AppendLine($"get() -> {visitor.ApplyTopRecord(records[0].Data, recordName)}.");
                break;
            }
            case TableMode.LIST:
            {
                if (table.IndexList.Count == 0)
                {
                    sb.AppendLine("-export([all/0]).");
                    sb.AppendLine();
                    AppendAllRecords(sb, records, recordName, format, tableNaming);
                    break;
                }
                sb.AppendLine("-export([get/1, all/0]).");
                sb.AppendLine();
                var visitor = new ToErlangLiteralVisitor(format, tableNaming);
                foreach (Record r in records)
                {
                    sb.AppendLine($"get({FormatIndexKey(r, table, visitor)}) -> {visitor.ApplyTopRecord(r.Data, recordName)};");
                }
                sb.AppendLine("get(_) -> none.");
                sb.AppendLine();
                AppendAllKeys(sb, records, table, format, tableNaming);
                break;
            }
            default:
                throw new NotSupportedException($"erlang target: unknown table mode {table.Mode}");
        }

        return sb.ToString();
    }

    private ErlangRecordFormat ResolveRecordFormat(DefTable table)
    {
        string tagValue = table.GetTag(RecordFormatTag);
        if (tagValue != null)
        {
            return ErlangRecordFormats.Parse(tagValue, $"table {table.FullName} tags");
        }
        string optionValue = EnvManager.Current.GetOptionOrDefault(Name, RecordFormatOption, true, null);
        if (optionValue != null)
        {
            return ErlangRecordFormats.Parse(optionValue, $"option {RecordFormatTag}");
        }
        return ErlangRecordFormat.FormattedRecord;
    }

    /// <summary>
    /// 解析 erlang.split tag。未配置返回 null；配置了但表模式或取模字段类型不支持、取值非法时报错。
    /// </summary>
    private int? ResolveSplitCount(DefTable table)
    {
        string tagValue = table.GetTag(SplitTag);
        if (tagValue == null)
        {
            return null;
        }
        if (table.Mode != TableMode.MAP && !(table.Mode == TableMode.LIST && table.IndexList.Count > 0))
        {
            throw new LubanException(table.Source, "error.erlang.split_unsupported_mode", table.FullName, table.Mode.ToString().ToLowerInvariant());
        }
        if (!int.TryParse(tagValue, out int splitCount) || splitCount < 2)
        {
            throw new LubanException(table.Source, "error.erlang.invalid_split_count", table.FullName, tagValue);
        }
        var index = table.IndexList[0];
        if (index.Type is not (TByte or TShort or TInt or TLong or TEnum))
        {
            throw new LubanException(table.Source, "error.erlang.split_unsupported_key_type", table.FullName, index.IndexField.Name, index.IndexField.Type);
        }
        return splitCount;
    }

    /// <summary>
    /// 生成门面模块：get/1 按主键（或索引首字段）对 N 归一化取模路由到子模块，all/0 聚合各子模块结果。
    /// </summary>
    private string BuildFacadeModule(DefTable table, ErlangTableNaming tableNaming, int splitCount)
    {
        string moduleName = tableNaming.ModuleName;
        // 多字段索引的 get/1 参数为 tuple，取模字段用 element/2 取首字段
        bool tupleKey = table.IndexList.Count > 1;
        string modExpr = tupleKey ? "element(1, Key)" : "Key";

        var sb = new StringBuilder();
        sb.Append(CommonFileHeaders.AUTO_GENERATE_ERLANG);
        sb.AppendLine($"-module({ErlangFormat.Atom(moduleName)}).");
        sb.AppendLine();
        sb.AppendLine("-export([get/1, all/0]).");
        sb.AppendLine();
        sb.AppendLine(tupleKey
            ? $"get(Key) when is_tuple(Key), is_integer({modExpr}) ->"
            : "get(Key) when is_integer(Key) ->");
        sb.AppendLine($"    case (({modExpr} rem {splitCount}) + {splitCount}) rem {splitCount} of");
        for (int i = 0; i < splitCount; i++)
        {
            sb.AppendLine($"        {i} -> {ErlangFormat.Atom($"{moduleName}_{i}")}:get(Key){(i == splitCount - 1 ? "" : ";")}");
        }
        sb.AppendLine("    end;");
        sb.AppendLine("get(_) ->");
        sb.AppendLine("    none.");
        sb.AppendLine();
        sb.AppendLine("all() ->");
        var allCalls = Enumerable.Range(0, splitCount).Select(i => $"{ErlangFormat.Atom($"{moduleName}_{i}")}:all()");
        sb.AppendLine($"    lists:append([{string.Join(", ", allCalls)}]).");
        return sb.ToString();
    }

    /// <summary>
    /// 按取模字段值对 N 归一化取模，把记录分到 N 个子模块，与门面运行时的路由公式一致。
    /// </summary>
    private static List<Record>[] PartitionRecords(DefTable table, List<Record> records, int splitCount)
    {
        int fieldIndex = table.IndexList[0].IndexFieldIdIndex;
        var shards = Enumerable.Range(0, splitCount).Select(_ => new List<Record>()).ToArray();
        foreach (Record record in records)
        {
            long modValue = ExtractModValue(record.Data.Fields[fieldIndex]);
            int shard = (int)(((modValue % splitCount) + splitCount) % splitCount);
            shards[shard].Add(record);
        }
        return shards;
    }

    private static long ExtractModValue(DType data)
    {
        return data switch
        {
            DByte b => b.Value,
            DShort s => s.Value,
            DInt i => i.Value,
            DLong l => l.Value,
            DEnum e => e.Value,
            _ => throw new NotSupportedException($"erlang target: unsupported split mod field data {data?.TypeName}"),
        };
    }

    private static string FormatIndexKey(Record record, DefTable table, ToErlangLiteralVisitor visitor)
    {
        var keyValues = table.IndexList.Select(idx => record.Data.Fields[idx.IndexFieldIdIndex].Apply(visitor, idx.IndexField.CType));
        return keyValues.Count() == 1 ? keyValues.First() : $"{{{string.Join(", ", keyValues)}}}";
    }

    private static void AppendAllKeys(StringBuilder sb, List<Record> records, DefTable table, ErlangRecordFormat format, ErlangTableNaming tableNaming)
    {
        var visitor = new ToErlangLiteralVisitor(format, tableNaming);
        var keys = records.Select(r => FormatIndexKey(r, table, visitor));
        sb.AppendLine("all() ->");
        sb.AppendLine($"    [{string.Join(", ", keys)}].");
    }

    private static void AppendAllRecords(StringBuilder sb, List<Record> records, string recordName, ErlangRecordFormat format, ErlangTableNaming tableNaming)
    {
        sb.AppendLine("all() ->");
        var visitor = new ToErlangLiteralVisitor(format, tableNaming);
        var items = records.Select(r => visitor.ApplyTopRecord(r.Data, recordName, format == ErlangRecordFormat.FormattedRecord ? 4 : 0)).ToList();
        sb.AppendLine(format == ErlangRecordFormat.FormattedRecord
            ? $"    [{string.Join(",\n    ", items)}]."
            : $"    [{string.Join(",\n     ", items)}].");
    }
}
