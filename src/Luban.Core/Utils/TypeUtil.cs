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

namespace Luban.Utils;

public static class TypeUtil
{

    public static (string, string) SplitFullName(string fullName)
    {
        int index = fullName.LastIndexOf('.');
        return (fullName.Substring(0, index), fullName.Substring(index + 1));
    }

    public static string MakeFullName(Stack<object> path)
    {
        var reverse = new List<string>();
        int index = 0;
        foreach (var e in path.Reverse())
        {
            if (!(e is string))
            {
                reverse.Add("[" + e + "]");
            }
            else
            {
                // 索引 0 是 table名， 不应该加 .
                if (index >= 1)
                {
                    reverse.Add(".");
                }
                reverse.Add(e.ToString());
            }
            ++index;
        }
        return string.Join("", reverse);
    }

    public static string MakeCppNamespaceBegin(string module)
    {
        if (string.IsNullOrEmpty(module))
        {
            return "";
        }
        return string.Join("", module.Split('.').Select(n => $"namespace {n} {{"));
    }

    public static string MakeCppNamespaceEnd(string module)
    {
        if (string.IsNullOrEmpty(module))
        {
            return "";
        }
        return string.Join("", module.Split('.').Select(n => $"}}"));
    }

    public static string MakeCppFullName(string module, string name)
    {
        return string.Join("::", MakeFullName(module, name).Split('.'));
    }

    public static string MakeCppJoinedFullName(string module, string ueName)
    {
        return string.Join("", module.Split('.').Select(n => $"{n}_")) + ueName;
    }

    public static string MakeTypescriptNamespaceBegin(string module)
    {
        if (string.IsNullOrEmpty(module))
        {
            return "";
        }
        return string.Join("", module.Split('.').Select(n => $"export namespace {n} {{"));
    }

    public static string MakeTypescriptNamespaceEnd(string module)
    {
        if (string.IsNullOrEmpty(module))
        {
            return "";
        }
        return MakeCppNamespaceEnd(module);
    }

    public static string MakeFullName(string module, string name)
    {
        if (string.IsNullOrEmpty(module))
        {
            return name;
        }
        if (string.IsNullOrEmpty(name))
        {
            return module;
        }
        return module + "." + name;
    }

    public static string MakeGoPkgName(string module)
    {
        int index = module.LastIndexOf('.');
        return index >= 0 ? module.Substring(index + 1) : module;
    }

    public static string MakeGoNamespace(string module)
    {
        if (string.IsNullOrEmpty(module))
        {
            return "";
        }
        return string.Join("", module.Split('.').Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => UpperCaseFirstChar(s)));
    }

    public static string MakeGoFullName(string module, string name)
    {
        return MakeGoNamespace(module) + name;
    }

    public static string MakePyFullName(string module, string name)
    {
        return MakeFullName(module, name).Replace('.', '_');
    }

    public static string MakeGDScriptFullName(string module, string name)
    {
        return ToPascalCase(module.Replace('.', '_') + "_" + name);
    }

    public static string MakeRustFullName(string module, string name)
    {
        return MakeGoNamespace(module) + name;
    }

    public static string MakePbFullName(string module, string name)
    {
        return MakeGoNamespace(module) + name;
    }

    public static string MakeFlatBuffersFullName(string module, string name)
    {
        return MakeGoNamespace(module) + name;
    }

    public static string MakeJavascriptFullName(string module, string name)
    {
        return MakeGoNamespace(module) + name;
    }

    public static string MakeNamespace(string module, string subModule)
    {
        if (string.IsNullOrWhiteSpace(module))
        {
            return subModule;
        }
        if (string.IsNullOrWhiteSpace(subModule))
        {
            return module;
        }
        return module + "." + subModule;
    }

    /// <summary>
    ///  the return value in range [offset, 2^14) 
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public static uint ComputeProtoHashIdByName(string name)
    {
        uint id = 0;
        foreach (char c in name)
        {
            id = 31 * id + c;
        }

        uint maxId = 2 << 14;
        uint offset = 1000;
        return id % (maxId - offset) + offset;
    }

    public static int ComputeCfgHashIdByName(string name)
    {
        int id = 0;
        foreach (char c in name)
        {
            id = 31 * id + c;
        }

        return id;
    }

    private static readonly HashSet<string> s_reserveNames = new()
    {
        "end",
        "base",
        "super",
        "const",
        "is",
        "as",
        "of",
        "typeid",
        "typeof",
        "object",
        "ref",
        "out",
        "in",
        "os",
        "sb",
        "if",
        "ele",
        "new",
        "friend",
        "public",
        "protected",
        "private",
        "internal",
        "return",
        "static",
    };

    public static bool IsValidName(string name)
    {
        name = name.Trim();
        return name.Length > 0 && !s_reserveNames.Contains(name);
    }

    public static string UpperCaseFirstChar(string s)
    {
        return char.ToUpper(s[0]) + s.Substring(1);
    }

    public static string ToCamelCase(string name)
    {
        var words = name.Split('_').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        var s = new StringBuilder();
        s.Append(words[0]);
        for (int i = 1; i < words.Length; i++)
        {
            s.Append(UpperCaseFirstChar(words[i]));
        }
        return s.ToString();
    }

    public static string ToPascalCase(string name)
    {
        return string.Join("", name.Split('_').Where(s => !string.IsNullOrWhiteSpace(s)).Select(c => UpperCaseFirstChar(c)));
    }

    public static string ToUnderScores(string name)
    {
        return name;
    }

    /// <summary>
    /// 转换为小写 snake_case：连续大写视为一段，大写遇小写断词，数字前断词。
    /// 例如 TbItem→tb_item、HTTPServer→http_server、ItemID→item_id、TbItem2→tb_item_2；
    /// 已是 snake_case 的名字保持原样。
    /// </summary>
    public static string ToLowerSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '_')
            {
                // 输入中的下划线原样保留（含连续下划线与开头下划线）
                sb.Append('_');
                continue;
            }
            bool needBreak = false;
            if (i > 0)
            {
                char prev = name[i - 1];
                char next = i + 1 < name.Length ? name[i + 1] : '\0';
                if (char.IsUpper(c) && (char.IsLower(prev) || char.IsDigit(prev)))
                {
                    // 小写或数字之后的大写：aB → a_b、1B → 1_b
                    needBreak = true;
                }
                else if (char.IsUpper(c) && char.IsUpper(prev) && next != '\0' && char.IsLower(next))
                {
                    // 连续大写的末尾且后面跟小写：ABc → a_bc
                    needBreak = true;
                }
                else if (char.IsDigit(c) && char.IsLetter(prev))
                {
                    // 字母之后的数字：a2 → a_2
                    needBreak = true;
                }
            }
            if (needBreak)
            {
                sb.Append('_');
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static string ToCsStyleName(string orginName)
    {
        return string.Join("", orginName.Split('_').Where(s => !string.IsNullOrWhiteSpace(s)).Select(c => UpperCaseFirstChar(c)));
    }

    public static string ToJavaStyleName(string orginName)
    {
        var words = orginName.Split('_').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        var s = new StringBuilder();
        s.Append(words[0]);
        for (int i = 1; i < words.Length; i++)
        {
            s.Append(UpperCaseFirstChar(words[i]));
        }
        return s.ToString();
    }

    public static string ToJavaGetterName(string orginName)
    {
        var words = orginName.Split('_').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        var s = new StringBuilder("get");
        foreach (var word in words)
        {
            s.Append(UpperCaseFirstChar(word));
        }
        return s.ToString();
    }

    public static string ToConventionGetterName(string orginName)
    {
        var words = orginName.Split('_').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        var s = new StringBuilder("get");
        foreach (var word in words)
        {
            s.Append(UpperCaseFirstChar(word));
        }
        return s.ToString();
    }

    public static string GetNamespace(string fullName)
    {
        var index = fullName.LastIndexOf('.');
        return index >= 0 ? fullName.Substring(0, index) : "";
    }


    public static string GetName(string fullName)
    {
        var index = fullName.LastIndexOf('.');
        return index >= 0 ? fullName.Substring(index + 1, fullName.Length - index - 1) : fullName;
    }
}
