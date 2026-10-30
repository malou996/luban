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
using Luban.Erlang.DataVisitors;
using System.Text;

namespace Luban.Erlang.DataTarget;

/// <summary>
/// 每张表导出一个 Erlang 源文件。
/// map 表：get/1 按主键返回 record，未匹配返回 none，all/0 返回全部主键；
/// list 表：有唯一索引时 get/1 按索引返回 record（多字段索引为 tuple），未匹配返回 none，all/0 返回全部索引；无索引时 all/0 返回全部记录；
/// 单例表：get/0 返回唯一记录。
/// record 值的渲染格式由 erlang.recordFormat 控制（formatted_record / online_record / online_tuple），
/// 表定义 tags 上的 erlang.recordFormat 优先于 xargs 选项，缺省 formatted_record。
/// record 定义在同名 .hrl 头文件中（由 erlang-record code target 生成）。
/// </summary>
[DataTarget("erlang")]
public class ErlangDataTarget : DataTargetBase
{
    private const string RecordFormatTag = "erlang.recordFormat";
    private const string RecordFormatOption = "recordFormat";

    protected override string DefaultOutputFileExt => "erl";

    public override OutputFile ExportTable(DefTable table, List<Record> records)
    {
        var valueBean = table.ValueTType.DefBean;

        string moduleName = table.OutputDataFile;
        // 抽象值表（多态行）没有统一的表 record，各记录按具体子类型的 record 名渲染
        string recordName = valueBean.IsAbstractType ? null : ErlangFormat.Atom(moduleName);
        var format = ResolveRecordFormat(table);

        var sb = new StringBuilder();
        sb.Append(CommonFileHeaders.AUTO_GENERATE_ERLANG);
        sb.AppendLine($"-module({ErlangFormat.Atom(moduleName)}).");
        sb.AppendLine($"-include(\"{moduleName}.hrl\").");
        sb.AppendLine();

        switch (table.Mode)
        {
            case TableMode.MAP:
            {
                sb.AppendLine("-export([get/1, all/0]).");
                sb.AppendLine();
                var visitor = new ToErlangLiteralVisitor(format);
                foreach (Record r in records)
                {
                    sb.AppendLine($"get({FormatIndexKey(r, table, visitor)}) -> {visitor.ApplyTopRecord(r.Data, recordName)};");
                }
                sb.AppendLine("get(_) -> none.");
                sb.AppendLine();
                AppendAllKeys(sb, records, table, format);
                break;
            }
            case TableMode.ONE:
            {
                sb.AppendLine("-export([get/0]).");
                sb.AppendLine();
                var visitor = new ToErlangLiteralVisitor(format);
                sb.AppendLine($"get() -> {visitor.ApplyTopRecord(records[0].Data, recordName)}.");
                break;
            }
            case TableMode.LIST:
            {
                if (table.IndexList.Count == 0)
                {
                    sb.AppendLine("-export([all/0]).");
                    sb.AppendLine();
                    AppendAllRecords(sb, records, recordName, format);
                    break;
                }
                sb.AppendLine("-export([get/1, all/0]).");
                sb.AppendLine();
                var visitor = new ToErlangLiteralVisitor(format);
                foreach (Record r in records)
                {
                    sb.AppendLine($"get({FormatIndexKey(r, table, visitor)}) -> {visitor.ApplyTopRecord(r.Data, recordName)};");
                }
                sb.AppendLine("get(_) -> none.");
                sb.AppendLine();
                AppendAllKeys(sb, records, table, format);
                break;
            }
            default:
                throw new NotSupportedException($"erlang target: unknown table mode {table.Mode}");
        }

        return CreateOutputFile($"{moduleName}.{OutputFileExt}", sb.ToString());
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

    private static string FormatIndexKey(Record record, DefTable table, ToErlangLiteralVisitor visitor)
    {
        var keyValues = table.IndexList.Select(idx => record.Data.Fields[idx.IndexFieldIdIndex].Apply(visitor));
        return keyValues.Count() == 1 ? keyValues.First() : $"{{{string.Join(", ", keyValues)}}}";
    }

    private static void AppendAllKeys(StringBuilder sb, List<Record> records, DefTable table, ErlangRecordFormat format)
    {
        var visitor = new ToErlangLiteralVisitor(format);
        var keys = records.Select(r => FormatIndexKey(r, table, visitor));
        sb.AppendLine("all() ->");
        sb.AppendLine($"    [{string.Join(", ", keys)}].");
    }

    private static void AppendAllRecords(StringBuilder sb, List<Record> records, string recordName, ErlangRecordFormat format)
    {
        sb.AppendLine("all() ->");
        var visitor = new ToErlangLiteralVisitor(format);
        var items = records.Select(r => visitor.ApplyTopRecord(r.Data, recordName, format == ErlangRecordFormat.FormattedRecord ? 4 : 0)).ToList();
        sb.AppendLine(format == ErlangRecordFormat.FormattedRecord
            ? $"    [{string.Join(",\n    ", items)}]."
            : $"    [{string.Join(",\n     ", items)}].");
    }
}
