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

using Luban.DataLoader;
using Luban.Datas;
using Luban.DataVisitors;
using Luban.Defs;
using Luban.Utils;
using System.Text;

namespace Luban.Erlang.DataVisitors;

/// <summary>
/// 把数据渲染为 Erlang term 字面量。
/// formatted_record：record 多行缩进排版，嵌套 record 递归展开，list、map（含抽象 bean 的 map）保持单行；
/// online_record：record 单行紧凑字面量；
/// online_tuple：具体 bean 渲染为 {record名, 各字段值...}，嵌套具体 bean 递归转换，抽象 bean 仍为带类型 tag 的 map。
/// 每个 ExportTable 调用使用独立实例（TopRecordName 有状态）。
/// </summary>
public class ToErlangLiteralVisitor : ToLiteralVisitorBase
{
    private const int IndentSize = 4;

    // 最外层表行的 record 名；抽象值表（行类型由具体子类型决定）为 null
    private string _topRecordName;

    // 表行即使声明类型为抽象（多态行）也渲染为具体子类型的 record
    private bool _topForceRecord;

    private readonly ErlangRecordFormat _format;

    // formatted_record 模式下当前 record 起始行的缩进空格数
    private int _indent;

    // list、map 内部的 record 保持单行渲染
    private int _inlineDepth;

    public ToErlangLiteralVisitor(ErlangRecordFormat format)
    {
        _format = format;
    }

    /// <summary>
    /// 以指定的 record 名渲染最外层 bean。recordName 传 null 表示抽象值表，
    /// 此时按各记录具体类型的 record 名渲染。indent 为 formatted_record 模式下
    /// record 起始行的缩进空格数。
    /// </summary>
    public string ApplyTopRecord(DBean bean, string recordName, int indent = 0)
    {
        _topRecordName = recordName;
        _topForceRecord = true;
        _indent = indent;
        try
        {
            return Accept(bean);
        }
        finally
        {
            _topRecordName = null;
            _topForceRecord = false;
            _indent = 0;
        }
    }

    public override string Accept(DString type)
    {
        return ErlangFormat.EscapeBinaryString(type.Value);
    }

    public override string Accept(DFloat type)
    {
        return ErlangFormat.FormatFloat(type.Value);
    }

    public override string Accept(DDouble type)
    {
        return ErlangFormat.FormatDouble(type.Value);
    }

    public override string Accept(DBean type)
    {
        if (type.Type.IsAbstractType && !_topForceRecord)
        {
            var x = new StringBuilder();
            x.Append("#{").Append(ErlangFormat.Atom(FieldNames.ErlangTypeNameKey)).Append(" => ")
                .Append(ErlangFormat.Atom(ErlangFormat.RecordName(type.ImplType)));
            int index = 0;
            _inlineDepth++;
            try
            {
                foreach (var f in type.Fields)
                {
                    var defField = (DefField)type.ImplType.HierarchyFields[index++];
                    if (f == null || !defField.NeedExport())
                    {
                        continue;
                    }
                    x.Append(", ").Append(ErlangFormat.Atom(defField.Name)).Append(" => ").Append(f.Apply(this));
                }
            }
            finally
            {
                _inlineDepth--;
            }
            x.Append('}');
            return x.ToString();
        }

        string recordName = _topRecordName ?? ErlangFormat.Atom(ErlangFormat.RecordName(type.ImplType));
        _topRecordName = null;
        _topForceRecord = false;

        if (_format == ErlangRecordFormat.OnlineTuple)
        {
            var tuple = new StringBuilder();
            tuple.Append('{').Append(recordName);
            AppendBeanFieldValues(type, tuple);
            tuple.Append('}');
            return tuple.ToString();
        }

        var sb = new StringBuilder();
        sb.Append('#').Append(recordName).Append('{');
        if (_format == ErlangRecordFormat.FormattedRecord && _inlineDepth == 0)
        {
            AppendBeanFieldsMultiline(type, sb);
        }
        else
        {
            AppendBeanFieldsInline(type, sb);
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>online_tuple：依次输出各字段值，每个值前补逗号加一个空格分隔，不写字段名。</summary>
    private void AppendBeanFieldValues(DBean type, StringBuilder sb)
    {
        int fieldIndex = 0;
        foreach (var f in type.Fields)
        {
            var defField = (DefField)type.ImplType.HierarchyFields[fieldIndex++];
            if (f == null || !defField.NeedExport())
            {
                continue;
            }
            sb.Append(", ").Append(f.Apply(this));
        }
    }

    /// <summary>online_record：各字段单行内联，name = value 形式。</summary>
    private void AppendBeanFieldsInline(DBean type, StringBuilder sb)
    {
        bool first = true;
        int fieldIndex = 0;
        foreach (var f in type.Fields)
        {
            var defField = (DefField)type.ImplType.HierarchyFields[fieldIndex++];
            if (f == null || !defField.NeedExport())
            {
                continue;
            }
            if (!first)
            {
                sb.Append(", ");
            }
            first = false;
            sb.Append(ErlangFormat.Atom(defField.Name)).Append(" = ").Append(f.Apply(this));
        }
    }

    /// <summary>
    /// formatted_record：每个字段独占一行，缩进为当前 record 起始行缩进加 4 个空格，
    /// 最后一个字段不带逗号，'}' 回到起始行缩进；字段值里的嵌套 record 递归展开。
    /// </summary>
    private void AppendBeanFieldsMultiline(DBean type, StringBuilder sb)
    {
        int fieldCount = 0;
        int fieldIndex = 0;
        foreach (var f in type.Fields)
        {
            var defField = (DefField)type.ImplType.HierarchyFields[fieldIndex++];
            if (f == null || !defField.NeedExport())
            {
                continue;
            }
            sb.Append(fieldCount > 0 ? ",\n" : "\n");
            sb.Append(' ', _indent + IndentSize)
                .Append(ErlangFormat.Atom(defField.Name)).Append(" = ");
            int savedIndent = _indent;
            _indent += IndentSize;
            sb.Append(f.Apply(this));
            _indent = savedIndent;
            fieldCount++;
        }
        if (fieldCount == 0)
        {
            return;
        }
        sb.Append('\n').Append(' ', _indent);
    }

    private void AppendList(List<DType> datas, StringBuilder x)
    {
        x.Append('[');
        bool first = true;
        _inlineDepth++;
        try
        {
            foreach (var e in datas)
            {
                if (!first)
                {
                    x.Append(", ");
                }
                first = false;
                x.Append(e.Apply(this));
            }
        }
        finally
        {
            _inlineDepth--;
        }
        x.Append(']');
    }

    public override string Accept(DArray type)
    {
        var x = new StringBuilder();
        AppendList(type.Datas, x);
        return x.ToString();
    }

    public override string Accept(DList type)
    {
        var x = new StringBuilder();
        AppendList(type.Datas, x);
        return x.ToString();
    }

    public override string Accept(DSet type)
    {
        var x = new StringBuilder();
        AppendList(type.Datas, x);
        return x.ToString();
    }

    public override string Accept(DMap type)
    {
        var x = new StringBuilder();
        x.Append("#{");
        bool first = true;
        _inlineDepth++;
        try
        {
            foreach (var e in type.DataMap)
            {
                if (!first)
                {
                    x.Append(", ");
                }
                first = false;
                x.Append(e.Key.Apply(this)).Append(" => ").Append(e.Value.Apply(this));
            }
        }
        finally
        {
            _inlineDepth--;
        }
        x.Append('}');
        return x.ToString();
    }
}
