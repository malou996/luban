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
using Luban.CodeTarget;
using Luban.Datas;
using Luban.DataTarget;
using Luban.Defs;
using Luban.Diagnostics;
using Luban.Types;
using System.Linq;
using System.Text;

namespace Luban.Erlang.DataTarget;

/// <summary>
/// 表级 tag erlang.macro 的宏定义头文件导出：每张配置了该 tag 的表导出一个 .hrl，
/// 每行数据生成一条 -define(宏名, 宏值).，宏排列保持表格行顺序，未配置 tag 的表不产出任何文件。
/// tag 参数见 ErlangMacroTagParams。宏名列、注释列须为 string 字段，单元格内容原样输出；
/// 宏值列类型决定渲染：整型家族→整数、float/double→浮点、string→utf8 binary、bool→true/false，
/// value 参数的 :atom 提示把 string 字段按 atom 输出，其余类型导出时报错。
/// 文件名由 output 参数指定（原样使用），缺省用该表的 erlang 模块名（与 .erl 模块、record 头文件同一命名推导）；
/// include guard 为 LUBAN_&lt;文件名大写&gt;_HRL，注释纵向排在最长 define 行之后，样式与 erlang-record 一致。
/// 输出目录为 erlang-macro target 的 outputDataDir（可在 luban.conf 配置成与 erlang-record 相同目录）；
/// 两张表生成同名文件时报错。
/// </summary>
[DataTarget("erlang-macro")]
public class ErlangMacroDataTarget : DataTargetBase
{
    // 并行的表导出任务共享同一实例做输出文件名冲突检测
    private readonly ConcurrentDictionary<string, string> _fileNameOwners = new();

    private readonly ErlangNaming _naming = ErlangNaming.Create();

    protected override string DefaultOutputFileExt => "hrl";

    public override OutputFile ExportTable(DefTable table, List<Record> records)
    {
        throw new NotSupportedException();
    }

    public override IReadOnlyList<OutputFile> ExportTableFiles(DefTable table, List<Record> records)
    {
        string tagValue = table.GetTag(ErlangMacroTagParams.MacroTag);
        if (tagValue == null)
        {
            return Array.Empty<OutputFile>();
        }
        var tag = ErlangMacroTagParams.Parse(tagValue, $"table '{table.FullName}' tags");
        var binding = ResolveFields(table, tag);
        string fileName = tag.OutputFile ?? _naming.ForTable(table).ModuleName;
        CheckFileName(table, fileName);
        return new[] { CreateOutputFile($"{fileName}.{OutputFileExt}", BuildFile(table, binding, records, fileName)) };
    }

    /// <summary>按 tag 参数解析值 bean 上的宏名/宏值/注释字段并校验类型，返回字段与数据下标绑定。</summary>
    private static MacroFieldBinding ResolveFields(DefTable table, ErlangMacroTagParams tag)
    {
        DefBean valueBean = table.ValueTType.DefBean;
        var binding = new MacroFieldBinding { ValueAsAtom = tag.ValueAsAtom };

        if (!valueBean.TryGetField(tag.NameField, out binding.NameField, out binding.NameIndex))
        {
            throw new LubanException(table.Source, "error.erlang.macro_field_not_exist", table.FullName, tag.NameField, valueBean.FullName);
        }
        if (binding.NameField.CType is not TString)
        {
            throw new LubanException(table.Source, "error.erlang.macro_name_not_string", table.FullName, tag.NameField, binding.NameField.CType);
        }

        if (!valueBean.TryGetField(tag.ValueField, out binding.ValueField, out binding.ValueIndex))
        {
            throw new LubanException(table.Source, "error.erlang.macro_field_not_exist", table.FullName, tag.ValueField, valueBean.FullName);
        }
        if (binding.ValueField.CType is not (TBool or TByte or TShort or TInt or TLong or TFloat or TDouble or TString))
        {
            throw new LubanException(table.Source, "error.erlang.macro_value_unsupported_type", table.FullName, tag.ValueField, binding.ValueField.CType);
        }
        if (tag.ValueAsAtom && binding.ValueField.CType is not TString)
        {
            throw new LubanException(table.Source, "error.erlang.macro_atom_requires_string", table.FullName, tag.ValueField, binding.ValueField.CType);
        }

        if (tag.CommentField != null)
        {
            if (!valueBean.TryGetField(tag.CommentField, out binding.CommentField, out binding.CommentIndex))
            {
                throw new LubanException(table.Source, "error.erlang.macro_field_not_exist", table.FullName, tag.CommentField, valueBean.FullName);
            }
            if (binding.CommentField.CType is not TString)
            {
                throw new LubanException(table.Source, "error.erlang.macro_comment_not_string", table.FullName, tag.CommentField, binding.CommentField.CType);
            }
        }
        return binding;
    }

    private string BuildFile(DefTable table, MacroFieldBinding binding, List<Record> records, string fileName)
    {
        string guard = ErlangFormat.MacroName($"luban_{fileName}_hrl");
        // 宏名 → 首次出现的数据来源，用于重名报错
        var nameOwners = new Dictionary<string, string>();
        var defines = new List<(string Def, string Comment)>();
        foreach (Record record in records)
        {
            string macroName = ReadMacroName(table, binding, record);
            if (!ErlangFormat.IsValidMacroName(macroName))
            {
                throw new LubanException(table.Source, "error.erlang.macro_invalid_name", table.FullName, record.Source, macroName);
            }
            if (nameOwners.TryGetValue(macroName, out string firstSource))
            {
                throw new LubanException(table.Source, "error.erlang.macro_duplicate_name", table.FullName, macroName, firstSource, record.Source);
            }
            nameOwners[macroName] = record.Source;

            DType valueData = record.Data.Fields[binding.ValueIndex];
            if (valueData == null)
            {
                throw new LubanException(table.Source, "error.erlang.macro_field_empty", table.FullName, record.Source, binding.ValueField.Name);
            }
            string literal = RenderValue(table, binding.ValueField, valueData, binding.ValueAsAtom);
            defines.Add(($"-define({macroName}, {literal}).", ReadComment(binding, record)));
        }

        var sb = new StringBuilder();
        sb.Append(CommonFileHeaders.AUTO_GENERATE_ERLANG);
        sb.AppendLine($"-ifndef({guard}).");
        sb.AppendLine($"-define({guard}, 1).");
        sb.AppendLine();
        if (defines.Count > 0)
        {
            // 注释 %% 所在列：最长 define 行之后留 2 个空格
            int commentColumn = defines.Max(d => d.Def.Length) + 2;
            foreach (var (def, comment) in defines)
            {
                sb.AppendLine(comment == null ? def : def.PadRight(commentColumn) + $"%% {comment}");
            }
            sb.AppendLine();
        }
        sb.AppendLine($"-endif. %% {guard}");
        return sb.ToString();
    }

    // 读取宏名：字段类型已校验为 string，取值为 null（可空字段为空）时报错
    private static string ReadMacroName(DefTable table, MacroFieldBinding binding, Record record)
    {
        if (record.Data.Fields[binding.NameIndex] is not DString s)
        {
            throw new LubanException(table.Source, "error.erlang.macro_field_empty", table.FullName, record.Source, binding.NameField.Name);
        }
        return s.Value;
    }

    // 读取注释：注释字段未配置或该行为空时返回 null；内容中的换行替换为空格
    private static string ReadComment(MacroFieldBinding binding, Record record)
    {
        if (binding.CommentField == null || record.Data.Fields[binding.CommentIndex] is not DString c)
        {
            return null;
        }
        string text = string.Join(' ', c.Value.Replace("\r\n", "\n").Replace("\r", "\n")
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        return text.Length == 0 ? null : text;
    }

    private static string RenderValue(DefTable table, DefField field, DType data, bool asAtom)
    {
        return data switch
        {
            DString s => asAtom ? ErlangFormat.Atom(s.Value) : ErlangFormat.EscapeBinaryString(s.Value),
            DByte v => v.Value.ToString(),
            DShort v => v.Value.ToString(),
            DInt v => v.Value.ToString(),
            DLong v => v.Value.ToString(),
            DFloat v => ErlangFormat.FormatFloat(v.Value),
            DDouble v => ErlangFormat.FormatDouble(v.Value),
            DBool v => v.Value ? "true" : "false",
            _ => throw new LubanException(table.Source, "error.erlang.macro_value_unsupported_type", table.FullName, field.Name, data.TypeName),
        };
    }

    /// <summary>登记输出文件名；同名不同源（另一张表已占用）时报错。</summary>
    private void CheckFileName(DefTable table, string fileName)
    {
        if (!_fileNameOwners.TryAdd(fileName, table.FullName) && _fileNameOwners[fileName] != table.FullName)
        {
            throw new LubanException(table.Source, "error.erlang.macro_file_conflict", fileName, _fileNameOwners[fileName], table.FullName);
        }
    }

    /// <summary>宏名/宏值/注释字段与数据下标的绑定；注释字段未配置时 CommentField 为 null。</summary>
    private sealed class MacroFieldBinding
    {
        public DefField NameField;
        public int NameIndex;
        public DefField ValueField;
        public int ValueIndex;
        public DefField CommentField;
        public int CommentIndex = -1;
        public bool ValueAsAtom;
    }
}
