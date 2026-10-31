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
using System.Linq;

namespace Luban.Erlang;

/// <summary>
/// 表级 tag erlang.macro 的参数。写法：
/// erlang.macro=name:&lt;宏名字段&gt;,value:&lt;宏值字段&gt;[:atom][,comment:&lt;注释字段&gt;][,output:&lt;文件名&gt;]
/// 参数对用 ':' 或 '=' 分隔（取第一个出现的分隔符），参数之间用逗号分隔。
/// name/value 必填；value 第三段 :atom 把 string 字段按 atom 输出；
/// output 原样作为 .hrl 文件名（不含扩展名），缺省由 target 取该表的 erlang 模块名。
/// </summary>
public sealed class ErlangMacroTagParams
{
    public const string MacroTag = "erlang.macro";

    private const string AtomHint = "atom";

    private static readonly HashSet<string> s_knownParams = new() { "name", "value", "comment", "output" };

    public string NameField { get; private set; }

    public string ValueField { get; private set; }

    public bool ValueAsAtom { get; private set; }

    public string CommentField { get; private set; }

    public string OutputFile { get; private set; }

    /// <summary>
    /// 解析 tag 值里的参数列表。context 为报错定位信息（如 table 'TbX' tags）。
    /// </summary>
    public static ErlangMacroTagParams Parse(string tagValue, string context)
    {
        // DefUtil.ParseAttrs 对无值 tag 会把值设为 key 本身，与空值一并按缺参处理
        if (string.IsNullOrWhiteSpace(tagValue) || tagValue == MacroTag)
        {
            throw new LubanException("error.erlang.macro_missing_param", context, "name");
        }

        var result = new ErlangMacroTagParams();
        var seen = new HashSet<string>();
        foreach (string rawToken in tagValue.Split(','))
        {
            string token = rawToken.Trim();
            if (token.Length == 0)
            {
                throw new LubanException("error.erlang.macro_empty_param", context);
            }
            int sep = token.IndexOfAny(new[] { ':', '=' });
            if (sep < 0)
            {
                throw new LubanException("error.erlang.macro_unknown_param", context, token);
            }
            string key = token[..sep].Trim();
            string value = token[(sep + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0)
            {
                throw new LubanException("error.erlang.macro_empty_param", context);
            }
            if (!s_knownParams.Contains(key))
            {
                throw new LubanException("error.erlang.macro_unknown_param", context, key);
            }
            if (!seen.Add(key))
            {
                throw new LubanException("error.erlang.macro_duplicate_param", context, key);
            }

            switch (key)
            {
                case "name":
                    result.NameField = value;
                    break;
                case "comment":
                    result.CommentField = value;
                    break;
                case "output":
                    ValidateOutputName(value, context);
                    result.OutputFile = value;
                    break;
                case "value":
                {
                    int hintSep = value.IndexOf(':');
                    if (hintSep < 0)
                    {
                        result.ValueField = value;
                        break;
                    }
                    string field = value[..hintSep].Trim();
                    string hint = value[(hintSep + 1)..].Trim();
                    if (field.Length == 0 || hint.Length == 0)
                    {
                        throw new LubanException("error.erlang.macro_empty_param", context);
                    }
                    if (hint != AtomHint)
                    {
                        throw new LubanException("error.erlang.macro_invalid_value_hint", context, hint);
                    }
                    result.ValueField = field;
                    result.ValueAsAtom = true;
                    break;
                }
            }
        }
        result.NameField ??= ThrowMissingParam(context, "name");
        result.ValueField ??= ThrowMissingParam(context, "value");
        return result;
    }

    // output 原样作为文件名使用：拒绝空白与文件名非法字符
    private static void ValidateOutputName(string name, string context)
    {
        if (name is "." or ".." || name.Any(char.IsWhiteSpace) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new LubanException("error.erlang.macro_invalid_output", context, name);
        }
    }

    private static string ThrowMissingParam(string context, string param)
    {
        throw new LubanException("error.erlang.macro_missing_param", context, param);
    }
}
