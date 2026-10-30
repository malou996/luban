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
/// map 表：get/1 按主键返回 record，all/0 返回全部主键；
/// list 表：有唯一索引时 get/1 按索引返回 record（多字段索引为 tuple），all/0 返回全部索引；无索引时 all/0 返回全部记录；
/// 单例表：get/0 返回唯一记录。
/// record 定义在同名 .hrl 头文件中（由 erlang-record code target 生成）。
/// </summary>
[DataTarget("erlang")]
public class ErlangDataTarget : DataTargetBase
{
    protected override string DefaultOutputFileExt => "erl";

    public override OutputFile ExportTable(DefTable table, List<Record> records)
    {
        var valueBean = table.ValueTType.DefBean;

        string moduleName = table.OutputDataFile;
        // 抽象值表（多态行）没有统一的表 record，各记录按具体子类型的 record 名渲染
        string recordName = valueBean.IsAbstractType ? null : ErlangFormat.Atom(moduleName);

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
                var visitor = new ToErlangLiteralVisitor();
                foreach (Record r in records)
                {
                    sb.AppendLine($"get({FormatIndexKey(r, table, visitor)}) -> {visitor.ApplyTopRecord(r.Data, recordName)};");
                }
                sb.AppendLine("get(_) -> undefined.");
                sb.AppendLine();
                AppendAllKeys(sb, records, table);
                break;
            }
            case TableMode.ONE:
            {
                sb.AppendLine("-export([get/0]).");
                sb.AppendLine();
                var visitor = new ToErlangLiteralVisitor();
                sb.AppendLine($"get() -> {visitor.ApplyTopRecord(records[0].Data, recordName)}.");
                break;
            }
            case TableMode.LIST:
            {
                if (table.IndexList.Count == 0)
                {
                    sb.AppendLine("-export([all/0]).");
                    sb.AppendLine();
                    AppendAllRecords(sb, records, recordName);
                    break;
                }
                sb.AppendLine("-export([get/1, all/0]).");
                sb.AppendLine();
                var visitor = new ToErlangLiteralVisitor();
                foreach (Record r in records)
                {
                    sb.AppendLine($"get({FormatIndexKey(r, table, visitor)}) -> {visitor.ApplyTopRecord(r.Data, recordName)};");
                }
                sb.AppendLine("get(_) -> undefined.");
                sb.AppendLine();
                AppendAllKeys(sb, records, table);
                break;
            }
            default:
                throw new NotSupportedException($"erlang target: unknown table mode {table.Mode}");
        }

        return CreateOutputFile($"{moduleName}.{OutputFileExt}", sb.ToString());
    }

    private static string FormatIndexKey(Record record, DefTable table, ToErlangLiteralVisitor visitor)
    {
        var keyValues = table.IndexList.Select(idx => record.Data.Fields[idx.IndexFieldIdIndex].Apply(visitor));
        return keyValues.Count() == 1 ? keyValues.First() : $"{{{string.Join(", ", keyValues)}}}";
    }

    private static void AppendAllKeys(StringBuilder sb, List<Record> records, DefTable table)
    {
        var visitor = new ToErlangLiteralVisitor();
        var keys = records.Select(r => FormatIndexKey(r, table, visitor));
        sb.AppendLine("all() ->");
        sb.AppendLine($"    [{string.Join(", ", keys)}].");
    }

    private static void AppendAllRecords(StringBuilder sb, List<Record> records, string recordName)
    {
        sb.AppendLine("all() ->");
        var visitor = new ToErlangLiteralVisitor();
        var items = records.Select(r => visitor.ApplyTopRecord(r.Data, recordName)).ToList();
        sb.AppendLine($"    [{string.Join(",\n     ", items)}].");
    }
}
