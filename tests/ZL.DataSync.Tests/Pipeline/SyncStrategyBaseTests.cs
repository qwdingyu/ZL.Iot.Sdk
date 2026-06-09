using SqlSugar;
using ZL.DataSync.Config;
using ZL.DataSync.Infrastructure;
using ZL.DataSync.Pipeline;

namespace ZL.DataSync.Tests.Pipeline;

/// <summary>
/// SyncStrategyBase 的 ApplyFiltersAndTransforms 测试。
/// 通过内部子类暴露 protected 方法以便测试。
/// </summary>
public class SyncStrategyBaseTests
{
    // 本地测试 Logger
    private sealed class TestLogger : IStructuredLogger
    {
        public List<string> Messages { get; } = new();
        public IStructuredLogger ForSource(string source) => this;
        public void Info(string message) => Messages.Add($"INFO: {message}");
        public void Warning(string message) => Messages.Add($"WARN: {message}");
        public void Error(string message) => Messages.Add($"ERROR: {message}");
        public void Debug(string message) => Messages.Add($"DEBUG: {message}");
        public void Flush() { }
        public void Dispose() { }
    }

    /// <summary>
    /// 测试用子类，暴露 protected 的 ApplyFiltersAndTransforms 方法。
    /// </summary>
    private sealed class TestableSyncStrategy : SyncStrategyBase
    {
        public TestableSyncStrategy(RemoteTargetConfig target, IStructuredLogger logger)
            : base(target.Name, target.ConnectionString, SqlSugar.DbType.MySql, logger)
        {
        }

        public override Task<SyncReport> SyncTableAsync(
            string tableName,
            string? remoteTable,
            int batchSize,
            SqlSugarClient localDb,
            CancellationToken ct)
            => throw new NotSupportedException();

        public List<Dictionary<string, object?>> TestApplyFiltersAndTransforms(
            List<Dictionary<string, object?>> rows,
            RemoteTargetConfig? config)
        {
            return ApplyFiltersAndTransforms(rows, config);
        }
    }

    private RemoteTargetConfig CreateHttpTarget()
    {
        return new RemoteTargetConfig
        {
            Name = "TestTarget",
            Type = TargetType.Http,
            ConnectionString = "http://localhost:5000/upload"
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  ApplyFiltersAndTransforms - no config
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ApplyFiltersAndTransforms_NoConfig_ReturnsOriginalRows()
    {
        var strategy = new TestableSyncStrategy(CreateHttpTarget(), new TestLogger());
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "Alice", ["Age"] = 30 },
            new() { ["Name"] = "Bob", ["Age"] = 25 }
        };

        var result = strategy.TestApplyFiltersAndTransforms(rows, null);

        Assert.Same(rows, result);
    }

    [Fact]
    public void ApplyFiltersAndTransforms_NullCallbacks_ReturnsOriginalRows()
    {
        var strategy = new TestableSyncStrategy(CreateHttpTarget(), new TestLogger());
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "Alice" }
        };
        var target = CreateHttpTarget();
        target.DataFilter = null;
        target.DataTransform = null;

        var result = strategy.TestApplyFiltersAndTransforms(rows, target);

        Assert.Same(rows, result);
    }

    // ═══════════════════════════════════════════════════════════
    //  ApplyFiltersAndTransforms - filter only
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ApplyFiltersAndTransforms_FilterPassesAll_ReturnsAll()
    {
        var strategy = new TestableSyncStrategy(CreateHttpTarget(), new TestLogger());
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "Alice", ["Age"] = 30 },
            new() { ["Name"] = "Bob", ["Age"] = 25 }
        };

        var target = CreateHttpTarget();
        target.DataFilter = row => row.TryGetValue("Age", out var a) && (int)a! > 18;

        var result = strategy.TestApplyFiltersAndTransforms(rows, target);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ApplyFiltersAndTransforms_FilterRejectsSome_ReturnsFiltered()
    {
        var strategy = new TestableSyncStrategy(CreateHttpTarget(), new TestLogger());
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "Alice", ["Age"] = 30 },
            new() { ["Name"] = "Bob", ["Age"] = 15 },
            new() { ["Name"] = "Charlie", ["Age"] = 25 }
        };

        var target = CreateHttpTarget();
        target.DataFilter = row => row.TryGetValue("Age", out var a) && (int)a! >= 18;

        var result = strategy.TestApplyFiltersAndTransforms(rows, target);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r["Name"]?.ToString() == "Alice");
        Assert.Contains(result, r => r["Name"]?.ToString() == "Charlie");
        Assert.DoesNotContain(result, r => r["Name"]?.ToString() == "Bob");
    }

    [Fact]
    public void ApplyFiltersAndTransforms_FilterRejectsAll_ReturnsEmpty()
    {
        var strategy = new TestableSyncStrategy(CreateHttpTarget(), new TestLogger());
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "Bob", ["Age"] = 15 }
        };

        var target = CreateHttpTarget();
        target.DataFilter = row => false; // 全部拒绝

        var result = strategy.TestApplyFiltersAndTransforms(rows, target);

        Assert.Empty(result);
    }

    [Fact]
    public void ApplyFiltersAndTransforms_Filter_MissingKey_SkipsRow()
    {
        var strategy = new TestableSyncStrategy(CreateHttpTarget(), new TestLogger());
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "Alice" }, // 没有 Age 字段
            new() { ["Name"] = "Bob", ["Age"] = 25 }
        };

        var target = CreateHttpTarget();
        target.DataFilter = row => row.TryGetValue("Age", out var a) && (int)a! > 18;

        var result = strategy.TestApplyFiltersAndTransforms(rows, target);

        Assert.Single(result);
        Assert.Equal("Bob", result[0]["Name"]);
    }

    // ═══════════════════════════════════════════════════════════
    //  ApplyFiltersAndTransforms - transform only
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ApplyFiltersAndTransforms_Transform_ModifiesRows()
    {
        var strategy = new TestableSyncStrategy(CreateHttpTarget(), new TestLogger());
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "Alice", ["Age"] = 30 }
        };

        var target = CreateHttpTarget();
        target.DataTransform = row =>
        {
            if (row.TryGetValue("Name", out var name))
                row["Name"] = name?.ToString()?.ToUpper();
        };

        var result = strategy.TestApplyFiltersAndTransforms(rows, target);

        Assert.Equal("ALICE", result[0]["Name"]);
        Assert.Equal(30, result[0]["Age"]);
    }

    [Fact]
    public void ApplyFiltersAndTransforms_Transform_AddsNewField()
    {
        var strategy = new TestableSyncStrategy(CreateHttpTarget(), new TestLogger());
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "Alice" }
        };

        var target = CreateHttpTarget();
        target.DataTransform = row => row["Processed"] = true;

        var result = strategy.TestApplyFiltersAndTransforms(rows, target);

        Assert.True((bool)result[0]!["Processed"]);
    }

    [Fact]
    public void ApplyFiltersAndTransforms_Transform_DoesNotReturnSameReference()
    {
        var strategy = new TestableSyncStrategy(CreateHttpTarget(), new TestLogger());
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "Alice" }
        };

        var target = CreateHttpTarget();
        target.DataTransform = row => row["Extra"] = "value";

        var result = strategy.TestApplyFiltersAndTransforms(rows, target);

        // 转换后应创建新字典，不修改原始行
        Assert.False(rows.Contains(result[0]));
        Assert.Single(rows);
        Assert.DoesNotContain("Extra", rows[0].Keys);
        Assert.Equal("value", result[0]["Extra"]);
    }

    // ═══════════════════════════════════════════════════════════
    //  ApplyFiltersAndTransforms - filter + transform combined
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ApplyFiltersAndTransforms_FilterAndTransform_Combined()
    {
        var strategy = new TestableSyncStrategy(CreateHttpTarget(), new TestLogger());
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "Alice", ["Age"] = 30 },
            new() { ["Name"] = "Bob", ["Age"] = 15 },
            new() { ["Name"] = "Charlie", ["Age"] = 25 }
        };

        var target = CreateHttpTarget();
        target.DataFilter = row => row.TryGetValue("Age", out var a) && (int)a! >= 18;
        target.DataTransform = row =>
        {
            if (row.TryGetValue("Name", out var name))
                row["Name"] = name?.ToString()?.ToUpper();
            row["Transformed"] = true;
        };

        var result = strategy.TestApplyFiltersAndTransforms(rows, target);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r["Name"]?.ToString() == "ALICE" && Convert.ToBoolean(r["Transformed"]));
        Assert.Contains(result, r => r["Name"]?.ToString() == "CHARLIE" && Convert.ToBoolean(r["Transformed"]));
    }

    // ═══════════════════════════════════════════════════════════
    //  ApplyFiltersAndTransforms - empty rows
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ApplyFiltersAndTransforms_EmptyRows_ReturnsEmpty()
    {
        var strategy = new TestableSyncStrategy(CreateHttpTarget(), new TestLogger());
        var rows = new List<Dictionary<string, object?>>();

        var target = CreateHttpTarget();
        target.DataFilter = _ => true;

        var result = strategy.TestApplyFiltersAndTransforms(rows, target);

        Assert.Empty(result);
    }
}
