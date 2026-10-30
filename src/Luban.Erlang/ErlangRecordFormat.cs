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

using Luban.Diagnostics;

namespace Luban.Erlang;

/// <summary>
/// .erl 数据文件中 record 值的渲染格式，由 erlang.recordFormat 选项控制。
/// </summary>
public enum ErlangRecordFormat
{
    /// <summary>多行缩进排版，方便人类阅读。</summary>
    FormattedRecord,

    /// <summary>单行紧凑 record 字面量。</summary>
    OnlineRecord,

    /// <summary>tuple 形式 {record名, 各字段值...}。</summary>
    OnlineTuple,
}

public static class ErlangRecordFormats
{
    public static ErlangRecordFormat Parse(string value, string source)
    {
        switch (value)
        {
            case "formatted_record":
                return ErlangRecordFormat.FormattedRecord;
            case "online_record":
                return ErlangRecordFormat.OnlineRecord;
            case "online_tuple":
                return ErlangRecordFormat.OnlineTuple;
            default:
                throw new LubanException("error.erlang.invalid_record_format", source, value);
        }
    }
}
