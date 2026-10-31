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
using Luban.Defs;
using Luban.Types;
using System.Text;

namespace Luban.Erlang.CodeTarget;

/// <summary>
/// 每张表生成一个 .hrl 头文件，包含表记录与全部嵌套 bean 的 record 定义。
/// 文件与 record 均带 include guard，可被多次 include 或与其他表的 .hrl 共存。
/// </summary>
[CodeTarget("erlang-record")]
public class ErlangRecordCodeTarget : CodeTargetBase
{
    private static readonly HashSet<string> s_preservedKeyWords = new();

    // atom 名一律经过 ErlangFormat.Atom 引用处理，无需保留字拒绝
    protected override IReadOnlySet<string> PreservedKeyWords => s_preservedKeyWords;

    public override string FileHeader => CommonFileHeaders.AUTO_GENERATE_ERLANG;

    protected override string FileSuffixName => "hrl";

    public override void Handle(GenerationContext ctx, OutputFileManifest manifest)
    {
        var tasks = ctx.ExportTables.Select(table => Task.Run(() =>
        {
            string content = new HeaderFileGenerator(table).Generate();
            return CreateOutputFile($"{table.OutputDataFile}.hrl", content);
        })).ToArray();
        Task.WaitAll(tasks);
        foreach (var task in tasks)
        {
            manifest.AddFile(task.Result);
        }
    }

    public override void GenerateTables(GenerationContext ctx, List<DefTable> tables, CodeWriter writer)
    {
        throw new NotSupportedException();
    }

    public override void GenerateTable(GenerationContext ctx, DefTable table, CodeWriter writer)
    {
        throw new NotSupportedException();
    }

    public override void GenerateBean(GenerationContext ctx, DefBean bean, CodeWriter writer)
    {
        throw new NotSupportedException();
    }

    public override void GenerateEnum(GenerationContext ctx, DefEnum @enum, CodeWriter writer)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// 生成单张表的头文件内容。嵌套 bean 的 record 依赖优先输出，表 record 最后输出。
    /// </summary>
    private class HeaderFileGenerator
    {
        private readonly DefTable _table;

        private readonly HashSet<DefBean> _visited = new();

        // 当前递归栈上的 bean；字段类型引用到栈内 bean 说明存在环，类型注解退化为 any()
        private readonly HashSet<DefBean> _visiting = new();

        private readonly List<string> _recordBlocks = new();

        public HeaderFileGenerator(DefTable table)
        {
            _table = table;
        }

        public string Generate()
        {
            var valueBean = _table.ValueTType.DefBean;

            if (valueBean.IsAbstractType)
            {
                // 抽象值表：为全部具体子类型各生成一个 record，行数据按具体类型取相应 record
                foreach (DefBean child in valueBean.HierarchyNotAbstractChildren)
                {
                    CollectBean(child);
                }
            }
            else
            {
                foreach (var field in valueBean.HierarchyExportFields)
                {
                    CollectFromType(field.CType);
                }
                _recordBlocks.Add(GenRecordBlock(_table.OutputDataFile, valueBean, new HashSet<DefBean>()));
            }

            string moduleName = _table.OutputDataFile;
            string fileGuard = ErlangFormat.Atom($"luban_{moduleName}_hrl");
            var sb = new StringBuilder();
            sb.Append(CommonFileHeaders.AUTO_GENERATE_ERLANG);
            sb.AppendLine($"-ifndef({fileGuard}).");
            sb.AppendLine($"-define({fileGuard}, 1).");
            sb.AppendLine();
            foreach (var block in _recordBlocks)
            {
                sb.AppendLine(block);
            }
            sb.AppendLine($"-endif. %% {fileGuard}");
            return sb.ToString();
        }

        private void CollectFromType(TType type)
        {
            switch (type)
            {
                case TBean bean:
                {
                    if (bean.DefBean.IsAbstractType)
                    {
                        // 抽象类型字段渲染为 map，但具体子类型内部的具体 bean 字段会渲染为嵌套 record，
                        // 因此全部子类型（含其依赖）都需要 record 定义
                        foreach (DefBean child in bean.DefBean.HierarchyNotAbstractChildren)
                        {
                            CollectBean(child);
                        }
                    }
                    else
                    {
                        CollectBean(bean.DefBean);
                    }
                    break;
                }
                case TMap map:
                {
                    CollectFromType(map.KeyType);
                    CollectFromType(map.ValueType);
                    break;
                }
                case TArray:
                case TList:
                case TSet:
                {
                    CollectFromType(type.ElementType);
                    break;
                }
            }
        }

        private void CollectBean(DefBean bean)
        {
            if (_visited.Contains(bean) || _visiting.Contains(bean))
            {
                return;
            }
            _visiting.Add(bean);
            foreach (var field in bean.HierarchyExportFields)
            {
                CollectFromType(field.CType);
            }
            _visiting.Remove(bean);
            _visited.Add(bean);
            _recordBlocks.Add(GenRecordBlock(ErlangFormat.RecordName(bean), bean, new HashSet<DefBean>(_visiting)));
        }

        private string GenRecordBlock(string rawRecordName, DefBean bean, HashSet<DefBean> inProgress)
        {
            var fields = bean.HierarchyExportFields
                .Select(f => (Def: $"    {ErlangFormat.Atom(f.Name)} :: {TypeAnnotation(f.CType, inProgress)}", Comment: f.Comment))
                .ToList();
            // 字段注释的 %% 对齐列：最长字段定义行（含行尾逗号）之后留 2 个空格
            int commentColumn = fields.Count == 0 ? 0
                : fields.Select((f, i) => f.Def.Length + (i < fields.Count - 1 ? 1 : 0)).Max() + 2;
            var fieldLines = new List<string>();
            for (int i = 0; i < fields.Count; i++)
            {
                string def = fields[i].Def + (i < fields.Count - 1 ? "," : "");
                if (string.IsNullOrWhiteSpace(fields[i].Comment))
                {
                    fieldLines.Add(def);
                    continue;
                }
                var parts = CommentLines(fields[i].Comment);
                fieldLines.Add(def.PadRight(commentColumn) + CommentText(parts[0]));
                // 注释内容含换行时续行对齐到 %% 所在列
                string continuationIndent = new string(' ', commentColumn);
                foreach (string part in parts.Skip(1))
                {
                    fieldLines.Add(continuationIndent + CommentText(part));
                }
            }
            string guard = ErlangFormat.Atom($"luban_record_{rawRecordName}");
            var sb = new StringBuilder();
            sb.AppendLine($"-ifndef({guard}).");
            sb.AppendLine($"-define({guard}, 1).");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(bean.Comment))
            {
                foreach (string part in CommentLines(bean.Comment))
                {
                    sb.AppendLine(CommentText(part));
                }
            }
            sb.AppendLine($"-record({ErlangFormat.Atom(rawRecordName)}, {{");
            if (fieldLines.Count > 0)
            {
                sb.AppendLine(string.Join("\n", fieldLines));
            }
            sb.AppendLine("}).");
            sb.AppendLine();
            sb.AppendLine($"-endif. %% {guard}");
            return sb.ToString();
        }

        // 单条注释文本：内容为空时只输出 %%，非空时 %% 与内容之间留 1 个空格
        private static string CommentText(string text) => text.Length == 0 ? "%%" : $"%% {text}";

        // Comment 按换行切分，每段去掉首尾空白
        private static List<string> CommentLines(string comment)
        {
            return comment.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
                .Select(part => part.Trim())
                .ToList();
        }

        private static string TypeAnnotation(TType type, HashSet<DefBean> inProgress)
        {
            string core = type switch
            {
                TBool => "boolean()",
                TByte or TShort or TInt or TLong => "integer()",
                TFloat or TDouble => "float()",
                TEnum or TDateTime => "integer()",
                TString => "binary()",
                TBean bean => bean.DefBean.IsAbstractType
                    ? "map()"
                    : inProgress.Contains(bean.DefBean)
                        ? "any()"
                        : $"#{ErlangFormat.Atom(ErlangFormat.RecordName(bean.DefBean))}{{}}",
                TMap map => $"#{{{TypeAnnotation(map.KeyType, inProgress)} := {TypeAnnotation(map.ValueType, inProgress)}}}",
                TArray or TList or TSet => $"[{TypeAnnotation(type.ElementType, inProgress)}]",
                _ => throw new NotSupportedException($"erlang-record target: unsupported type {type.GetType().Name}"),
            };
            return type.IsNullable ? $"{core} | undefined" : core;
        }
    }
}
