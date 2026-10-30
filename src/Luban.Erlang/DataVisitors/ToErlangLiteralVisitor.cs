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
/// bean 输出为 record 字面量 #rec{field = value}；抽象类型字段输出为带类型 tag 的 map。
/// 每个 ExportTable 调用使用独立实例（TopRecordName 有状态）。
/// </summary>
public class ToErlangLiteralVisitor : ToLiteralVisitorBase
{
    // 最外层表行的 record 名；抽象值表（行类型由具体子类型决定）为 null
    private string _topRecordName;

    // 表行即使声明类型为抽象（多态行）也渲染为具体子类型的 record
    private bool _topForceRecord;

    /// <summary>
    /// 以指定的 record 名渲染最外层 bean。recordName 传 null 表示抽象值表，
    /// 此时按各记录具体类型的 record 名渲染。
    /// </summary>
    public string ApplyTopRecord(DBean bean, string recordName)
    {
        _topRecordName = recordName;
        _topForceRecord = true;
        try
        {
            return Accept(bean);
        }
        finally
        {
            _topRecordName = null;
            _topForceRecord = false;
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
            foreach (var f in type.Fields)
            {
                var defField = (DefField)type.ImplType.HierarchyFields[index++];
                if (f == null || !defField.NeedExport())
                {
                    continue;
                }
                x.Append(", ").Append(ErlangFormat.Atom(defField.Name)).Append(" => ").Append(f.Apply(this));
            }
            x.Append('}');
            return x.ToString();
        }

        string recordName = _topRecordName ?? ErlangFormat.Atom(ErlangFormat.RecordName(type.ImplType));
        _topRecordName = null;
        _topForceRecord = false;
        var sb = new StringBuilder();
        sb.Append('#').Append(recordName).Append('{');
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
        sb.Append('}');
        return sb.ToString();
    }

    private void AppendList(List<DType> datas, StringBuilder x)
    {
        x.Append('[');
        bool first = true;
        foreach (var e in datas)
        {
            if (!first)
            {
                x.Append(", ");
            }
            first = false;
            x.Append(e.Apply(this));
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
        foreach (var e in type.DataMap)
        {
            if (!first)
            {
                x.Append(", ");
            }
            first = false;
            x.Append(e.Key.Apply(this)).Append(" => ").Append(e.Value.Apply(this));
        }
        x.Append('}');
        return x.ToString();
    }
}
