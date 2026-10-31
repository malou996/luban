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

using System.Text;

namespace Luban.Erlang;

/// <summary>
/// Erlang 命名与字面量格式化工具。
/// </summary>
public static class ErlangFormat
{
    /// <summary>
    /// string 字段的类型 tag：值渲染为 atom、record 头注解为 atom()。schema 写法 string#erlang.atom。
    /// </summary>
    public const string AtomTag = "erlang.atom";

    /// <summary>
    /// Erlang 保留字，atom 与这些名字相同时必须加引号。
    /// </summary>
    private static readonly HashSet<string> s_reservedWords = new()
    {
        "after", "and", "andalso", "band", "begin", "bnot", "bor", "bsl", "bsr", "bxor",
        "case", "catch", "cond", "div", "end", "fun", "if", "let", "maybe", "else",
        "not", "of", "or", "orelse", "receive", "rem", "try", "when", "xor",
    };

    /// <summary>
    /// 输出 atom 字面量。合法的非保留字小写名字直接输出，其余加单引号。
    /// </summary>
    public static string Atom(string name)
    {
        if (IsUnquotedAtomName(name))
        {
            return name;
        }
        return "'" + name.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
    }

    private static bool IsUnquotedAtomName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }
        if (name[0] < 'a' || name[0] > 'z')
        {
            return false;
        }
        foreach (var c in name)
        {
            if (!(c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '@'))
            {
                return false;
            }
        }
        return !s_reservedWords.Contains(name);
    }

    /// <summary>
    /// 输出宏名：按 erlang 惯例整体大写（如 LUBAN_TB_ITEM_HRL）。
    /// 宏名允许大写开头（如 ?MODULE），合法字符集与 atom 相同；含其他字符时加单引号。
    /// </summary>
    public static string MacroName(string name)
    {
        string upper = name.ToUpperInvariant();
        if (IsUnquotedMacroName(upper))
        {
            return upper;
        }
        return "'" + upper.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
    }

    private static bool IsUnquotedMacroName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }
        if (!(name[0] is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z')))
        {
            return false;
        }
        foreach (var c in name)
        {
            if (!(c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '@'))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 判断名字能否作为宏名使用：字母开头，仅含字母、数字、_ 与 @。
    /// </summary>
    public static bool IsValidMacroName(string name)
    {
        return IsUnquotedMacroName(name);
    }

    /// <summary>
    /// 输出 utf8 binary 字面量，形如 &lt;&lt;"text"/utf8&gt;&gt;。非 ASCII 字符原样保留。
    /// </summary>
    public static string EscapeBinaryString(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\v':
                    sb.Append("\\v");
                    break;
                default:
                    if (c < 0x20 || c == 0x7f)
                    {
                        sb.Append("\\x{").Append(((int)c).ToString("X")).Append('}');
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return $"<<\"{sb}\"/utf8>>";
    }

    public static string FormatFloat(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new NotSupportedException($"erlang target: float value {value} is NaN or Infinity");
        }
        return NormalizeFloat(value.ToString("R"));
    }

    public static string FormatDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new NotSupportedException($"erlang target: double value {value} is NaN or Infinity");
        }
        return NormalizeFloat(value.ToString("R"));
    }

    /// <summary>
    /// Erlang 浮点字面量必须带小数点（"1." 会被当作整数），指数部分使用小写 e。
    /// </summary>
    private static string NormalizeFloat(string s)
    {
        s = s.Replace('E', 'e');
        int eIndex = s.IndexOf('e');
        string mantissa = eIndex < 0 ? s : s[..eIndex];
        string exponent = eIndex < 0 ? "" : s[eIndex..];
        if (!mantissa.Contains('.'))
        {
            mantissa += ".0";
        }
        return mantissa + exponent;
    }
}
