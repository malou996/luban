// Copyright 2025 Code Philosophy

using Luban.Utils;
using Xunit;

namespace Luban.Tests.Unit;

public class TypeUtilTests
{
    [Theory]
    [InlineData("TbItem", "tb_item")]
    [InlineData("GlobalConfig", "global_config")]
    [InlineData("startTime", "start_time")]
    // 连续大写视为一段，末尾大写遇小写断词
    [InlineData("HTTPServer", "http_server")]
    [InlineData("ItemID", "item_id")]
    [InlineData("ParseHTTPResponse", "parse_http_response")]
    // 数字前断词
    [InlineData("TbItem2", "tb_item_2")]
    [InlineData("Item22", "item_22")]
    // 已是 snake_case 的名字保持原样
    [InlineData("tb_item", "tb_item")]
    [InlineData("already_snake_name", "already_snake_name")]
    // 下划线不重复叠加
    [InlineData("TbItem_2", "tb_item_2")]
    [InlineData("a__b", "a__b")]
    // 单词与小写开头
    [InlineData("Item", "item")]
    [InlineData("item", "item")]
    public void ToLowerSnakeCase_Converts(string input, string expected)
    {
        Assert.Equal(expected, TypeUtil.ToLowerSnakeCase(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ToLowerSnakeCase_Empty_ReturnsAsIs(string input)
    {
        Assert.Equal(input, TypeUtil.ToLowerSnakeCase(input));
    }
}
