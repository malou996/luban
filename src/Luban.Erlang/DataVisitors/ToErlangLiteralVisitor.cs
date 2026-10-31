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
// IMPLIED, INCLUDING BUT NOT LIMITED TO WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIMS, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using Luban.DataLoader;
using Luban.Datas;
using Luban.DataVisitors;
using Luban.Defs;
using Luban.Types;
using Luban.Utils;
using System.Text;

namespace Luban.Erlang.DataVisitors;

/// <summary>
/// 把数据渲染为 Erlang term 字面量。
/// formatted_record：record 多行缩进排版，嵌套 record 递归展开，list、map（含抽象 bean 的 map）保持单行；
/// compact_record：record 单行紧凑字面量；
/// compact_tuple：具体 bean 渲染为 {record名, 各字段值...}，嵌套具体 bean 递归转换，抽象 bean 仍为带类型 tag 的 map。
/// record 名与字段名经 tableNaming 转换，与 .hrl 头文件保持一致。
/// string 字段带类型 tag erlang.atom 时其值渲染为 atom（作为索引键时同样适用），缺省渲染为 utf8 binary。
/// 每个 ExportTable 调用使用独立实例（TopRecordName 有状态）。
/// 分发走 IDataFuncVisitor2&lt;string&gt;：Apply 时携带 TType，string 的 atom tag 与容器元素类型都取自 TType。
/// </summary>
public class ToErlangLiteralVisitor : ToLiteralVisitorBase, IDataFuncVisitor2<string>
{
    private const int IndentSize = 4;

    // 单参分发缺少 TType 时的报错信息
    private const string MissingTypeDispatchError = "erlang literal rendering requires the 2-argument dispatch carrying TType";

    // 最外层表行的 record 名；抽象值表（行类型由具体子类型决定）为 null
    private string _topRecordName;

    // 表行即使声明类型为抽象（多态行）也渲染为具体子类型的 record
    private bool _topForceRecord;

    private readonly ErlangRecordFormat _format;

    private readonly ErlangTableNaming _naming;

    // formatted_record 模式下当前 record 起始行的缩进空格数
    private int _indent;

    // list、map 内部的 record 保持单行渲染
    private int _inlineDepth;

    public ToErlangLiteralVisitor(ErlangRecordFormat format, ErlangTableNaming naming)
    {
        _format = format;
        _naming = naming;
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

    // 标量渲染与 TType 无关，委托给单参版本
    public string Accept(DBool data, TType type) => Accept(data);

    public string Accept(DByte data, TType type) => Accept(data);

    public string Accept(DShort data, TType type) => Accept(data);

    public string Accept(DInt data, TType type) => Accept(data);

    public string Accept(DLong data, TType type) => Accept(data);

    public string Accept(DFloat data, TType type) => Accept(data);

    public string Accept(DDouble data, TType type) => Accept(data);

    public string Accept(DEnum data, TType type) => Accept(data);

    public string Accept(DDateTime data, TType type) => Accept(data);

    public string Accept(DString data, TType type)
    {
        // string#erlang.atom 的值渲染为 atom，其余渲染为 utf8 binary
        return type.HasTag(ErlangFormat.AtomTag) ? ErlangFormat.Atom(data.Value) : ErlangFormat.EscapeBinaryString(data.Value);
    }

    public string Accept(DBean data, TType type) => Accept(data);

    public string Accept(DArray data, TType type) => FormatList(data.Datas, type.ElementType);

    public string Accept(DList data, TType type) => FormatList(data.Datas, type.ElementType);

    public string Accept(DSet data, TType type) => FormatList(data.Datas, type.ElementType);

    public string Accept(DMap data, TType type)
    {
        var mapType = (TMap)type;
        var x = new StringBuilder();
        x.Append("#{");
        bool first = true;
        _inlineDepth++;
        try
        {
            foreach (var e in data.DataMap)
            {
                if (!first)
                {
                    x.Append(", ");
                }
                first = false;
                x.Append(e.Key.Apply(this, mapType.KeyType)).Append(" => ").Append(e.Value.Apply(this, mapType.ValueType));
            }
        }
        finally
        {
            _inlineDepth--;
        }
        x.Append('}');
        return x.ToString();
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
                .Append(ErlangFormat.Atom(_naming.RecordName(type.ImplType)));
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
                    x.Append(", ").Append(ErlangFormat.Atom(_naming.FieldName(defField.Name))).Append(" => ").Append(f.Apply(this, defField.CType));
                }
            }
            finally
            {
                _inlineDepth--;
            }
            x.Append('}');
            return x.ToString();
        }

        string recordName = _topRecordName ?? ErlangFormat.Atom(_naming.RecordName(type.ImplType));
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

    // string 与容器的单参分发缺少 TType（atom tag、元素类型都取不到），误入时快速失败
    public override string Accept(DString type) => throw new NotSupportedException(MissingTypeDispatchError);

    public override string Accept(DArray type) => throw new NotSupportedException(MissingTypeDispatchError);

    public override string Accept(DList type) => throw new NotSupportedException(MissingTypeDispatchError);

    public override string Accept(DSet type) => throw new NotSupportedException(MissingTypeDispatchError);

    public override string Accept(DMap type) => throw new NotSupportedException(MissingTypeDispatchError);

    /// <summary>compact_tuple：依次输出各字段值，每个值前补逗号加一个空格分隔，不写字段名。</summary>
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
            sb.Append(", ").Append(f.Apply(this, defField.CType));
        }
    }

    /// <summary>compact_record：各字段单行内联，name = value 形式。</summary>
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
            sb.Append(ErlangFormat.Atom(_naming.FieldName(defField.Name))).Append(" = ").Append(f.Apply(this, defField.CType));
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
                .Append(ErlangFormat.Atom(_naming.FieldName(defField.Name))).Append(" = ");
            int savedIndent = _indent;
            _indent += IndentSize;
            sb.Append(f.Apply(this, defField.CType));
            _indent = savedIndent;
            fieldCount++;
        }
        if (fieldCount == 0)
        {
            return;
        }
        sb.Append('\n').Append(' ', _indent);
    }

    private string FormatList(List<DType> datas, TType elementType)
    {
        var x = new StringBuilder();
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
                x.Append(e.Apply(this, elementType));
            }
        }
        finally
        {
            _inlineDepth--;
        }
        x.Append(']');
        return x.ToString();
    }
}
