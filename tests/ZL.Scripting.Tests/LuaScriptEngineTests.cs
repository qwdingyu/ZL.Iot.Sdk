using ZL.Scripting;

namespace ZL.Scripting.Tests;

/// <summary>
/// LuaScriptEngine 回归锁（NLua 1.7.3 / Lua 5.4）——此前该库零覆盖。
/// 断言都按源码语义锚定：数值统一 Convert 后比较（NLua 整数返回 long、小数返回 double）；
/// 取消路径刻意用「预取消令牌 + 中等长度脚本」：Wait(ct) 立即抛出，
/// 而脚本在 2s 兜底窗口内自然跑完，规避「后台线程仍在执行时 Dispose 状态」的危险路径。
/// </summary>
public class LuaScriptEngineTests : IDisposable
{
    private readonly LuaScriptEngine _engine = new();

    public void Dispose() => _engine.Dispose();

    [Fact]
    public void 执行脚本_算术与字符串返回()
    {
        object[] r1 = _engine.ExecuteScript("return 1 + 2");
        Assert.Equal(3, Convert.ToInt64(r1[0]));

        object[] r2 = _engine.ExecuteScript("return 'hello'");
        Assert.Equal("hello", r2[0]);
    }

    [Fact]
    public void 执行脚本_语法错误包装为InvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _engine.ExecuteScript("return )"));
        Assert.Contains("Lua script execution failed", ex.Message);
    }

    [Fact]
    public void SetGlobal_脚本可读取CSharp变量()
    {
        _engine.SetGlobal("magic", 42);
        object[] r = _engine.ExecuteScript("return magic");
        Assert.Equal(42, Convert.ToInt64(r[0]));
    }

    [Fact]
    public void 方法绑定_Lua可直接调用CSharp实例方法()
    {
        var ctx = new AddContext();
        object[] r = _engine.ExecuteScript("return csharp_add(2, 3)", ctx, ("csharp_add", "Add"));
        Assert.Equal(5, Convert.ToInt64(r[0]));
    }

    [Fact]
    public void 绑定_方法名不存在则报错()
    {
        var ctx = new AddContext();
        Assert.Throws<InvalidOperationException>(
            () => _engine.ExecuteScript("return 1", ctx, ("f", "NoSuchMethod")));
    }

    [Fact]
    public void ValidateScript_合法不抛非法抛()
    {
        _engine.ValidateScript("local x = 1 return x");
        Assert.ThrowsAny<Exception>(() => _engine.ValidateScript("return )"));
    }

    [Fact]
    public void ExecuteFile_从文件加载执行()
    {
        string path = Path.Combine(Path.GetTempPath(), $"zl-lua-{Guid.NewGuid():N}.lua");
        File.WriteAllText(path, "return 'fromfile'");
        try
        {
            object[] r = _engine.ExecuteFile(path, new object());
            Assert.Equal("fromfile", r[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExecuteFile_文件不存在抛FileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(
            () => _engine.ExecuteFile("/nonexistent/zl-lua.lua", new object()));
    }

    [Fact]
    public void 取消_预取消令牌立即中断并抛OperationCanceledException()
    {
        // 中等长度循环：Wait(ct) 因令牌已取消而立刻抛出，脚本随后在 2s 兜底窗口内自然完成，
        // 不会触碰「执行中 Dispose」的危险路径（见源码注释）。
        var ct = new CancellationToken(canceled: true);
        Assert.Throws<OperationCanceledException>(
            () => _engine.ExecuteScript("local s = 0 for i = 1, 5000000 do s = s + i end return s",
                new object(), ct));
    }

    [Fact]
    public void Dispose_幂等_之后调用抛ObjectDisposedException()
    {
        _engine.Dispose();
        _engine.Dispose(); // 幂等
        Assert.Throws<ObjectDisposedException>(() => _engine.ExecuteScript("return 1"));
    }

    private sealed class AddContext
    {
        public int Add(int a, int b) => a + b;
    }
}
