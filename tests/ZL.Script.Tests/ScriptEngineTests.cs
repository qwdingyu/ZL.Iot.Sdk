using ZL.DataProcessing;
using ZL.Script;
using Xunit;

namespace ZL.Script.Tests;

/// <summary>
/// 脚本/表达式引擎回归锁（2026-10-04 自 ZL.Simulator 移植，随三处修复一并归集）。
///
/// <para>
/// <b>背景</b>：`ZL.Script.ScriptEngine` 有两处缺陷，导致**表达式能力大面积不可用**，
/// 而既有测试没覆盖到（协议模板的 `=表达式` / 条件响应因此长期静默失败）：
/// </para>
/// <list type="number">
///   <item>
///     <b>实参个数不匹配</b>：`BuildArgs` 只按 `UsedParameters` 传参，而 DynamicExpresso 的
///     `Lambda.Invoke(object[])` 要求实参个数与**声明的形参**一致 → 任何未用满上下文的表达式
///     都抛 `Arguments count mismatch`。例如 `DataExpressionEngine.Process("= 1 + 2")`
///     （它恒传完整上下文）返回 `&lt;SCRIPT_ERROR: Failed to evaluate: = 1 + 2&gt;`。
///   </item>
///   <item>
///     <b>助手库注册方式错</b>：静态类被 `SetVariable(name, type)` 绑成 `Type` 对象，
///     于是 `Format.Hex(255)` 被解析成「对 Type 的实例调用 Hex」→
///     `No applicable method 'Hex' exists in type 'Type'`；Hex/Binary/Bit/Checksum/Crc/Format/Sim
///     七个工业助手库在表达式里全部不可用。
///   </item>
/// </list>
/// </summary>
public class ScriptEngineTests
{
    private static Dictionary<string, object> Context() => new()
    {
        ["Args"] = Array.Empty<string>(),
        ["ArgsText"] = string.Empty,
        ["Input"] = string.Empty,
    };

    [Fact]
    public void 带上下文求值_未用满形参也必须成功()
    {
        var engine = new ScriptEngine();

        // 表达式一个形参都没用到；修复前必抛 Arguments count mismatch
        Assert.Equal(3, engine.Evaluate("= 1 + 2", Context()));
    }

    [Fact]
    public void 带上下文求值_条件表达式可用()
    {
        var engine = new ScriptEngine();

        Assert.Equal(7, engine.Evaluate("= Input == \"\" ? 7 : 8", Context()));
        Assert.Equal(8, engine.Evaluate("= Input == \"\" ? 7 : 8", new Dictionary<string, object> { ["Input"] = "x" }));
    }

    [Fact]
    public void 空上下文求值_与无上下文一致()
    {
        var engine = new ScriptEngine();

        Assert.Equal(3, engine.Evaluate("= 1 + 2"));
        Assert.Equal(3, engine.Evaluate("= 1 + 2", new Dictionary<string, object>()));
    }

    [Fact]
    public void 缓存_同表达式不同上下文不得互相污染()
    {
        var engine = new ScriptEngine();

        // 先用「带 Input」的上下文解析并缓存，再用「不带 Input」的上下文求值：
        // 缓存键若只按表达式，就会复用带形参的 lambda 而调用方没有该值。
        Assert.Equal(1, engine.Evaluate("= Input == \"\" ? 1 : 2", Context()));
        Assert.Equal(3, engine.Evaluate("= 1 + 2", new Dictionary<string, object>()));
        Assert.Equal(1, engine.Evaluate("= Input == \"\" ? 1 : 2", Context()));
    }

    [Fact]
    public void 工业助手库_别名在表达式里可用()
    {
        var engine = new ScriptEngine();

        // 修复前：No applicable method 'Hex' exists in type 'Type'
        Assert.Equal("FF", engine.Evaluate("=Format.Hex(255)"));
        Assert.Equal("00FF", engine.Evaluate("=Format.Hex(255,\"X4\")"));
    }

    [Fact]
    public void DataExpressionEngine_前缀表达式可用()
    {
        var engine = new DataExpressionEngine();

        // 修复前返回 <SCRIPT_ERROR: Failed to evaluate: = 1 + 2>
        Assert.Equal("3", engine.Process("= 1 + 2", string.Empty, Array.Empty<string>()));
        Assert.Equal("2", engine.Process("=Math.Max(1,2)", string.Empty, Array.Empty<string>()));
        // 不带前缀的仍是 token 替换（语义不变）
        Assert.Equal("1+2", engine.Process("1+2", string.Empty, Array.Empty<string>()));
    }
}
