// ============================================================
//  GenerateJob 单元测试
//  覆盖：状态机转换、SSE 事件流、时间戳、清除结果
// ============================================================

using ZL.Iot.Runner.Configuration;
using ZL.Iot.Runner.Generator.Core.Models;

namespace ZL.Iot.Runner.Generator.Tests;

public class GenerateJobTests
{
    private GenerateJob CreateJob()
    {
        var request = new GenerateRequest
        {
            ProjectName = "TestJob",
            Version = "1.0.0",
            Sku = SkuMode.Source,
            Config = new RunnerConfig
            {
                Runner = new RunnerOptions { Name = "Test" },
                Devices = new List<DeviceProfile>
                {
                    new()
                    {
                        Code = "plc1",
                        Protocol = "SiemensS7",
                        Ip = "192.168.1.1",
                        Port = 102,
                        Tags = new List<TagProfile>
                        {
                            new() { Id = "T1", Address = "DB1.DBD0", DataType = "float", Enable = true, TagType = "D" }
                        },
                        Executors = new List<ExecutorProfile>()
                    }
                }
            }
        };
        return GenerateJob.Create(request, "user123");
    }

    [Fact]
    public void Create_InitializesCorrectly()
    {
        var job = CreateJob();

        Assert.NotEqual(Guid.Empty, job.Id);
        Assert.Equal("user123", job.UserId);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(1, job.QueuePosition);
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.ErrorMessage);
        Assert.Null(job.ResultBytes);
        Assert.Null(job.Elapsed);
    }

    [Fact]
    public void SetRunning_TransitionsCorrectly()
    {
        var job = CreateJob();
        job.SetRunning();

        Assert.Equal(JobStatus.Running, job.Status);
        Assert.NotNull(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.NotNull(job.StatusText);
        Assert.Contains("生成中", job.StatusText);
    }

    [Fact]
    public void SetSucceeded_TransitionsCorrectly()
    {
        var job = CreateJob();
        job.SetRunning();
        var bytes = new byte[] { 1, 2, 3 };
        var result = GenerateResult.Ok(bytes, "out.zip", TimeSpan.FromSeconds(3));
        job.SetSucceeded(bytes, "out.zip", result);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.NotNull(job.CompletedAt);
        Assert.Same(bytes, job.ResultBytes);
        Assert.Equal("out.zip", job.ResultFileName);
        Assert.NotNull(job.Result);
        Assert.NotNull(job.Elapsed);
        Assert.Contains("完成", job.StatusText);
    }

    [Fact]
    public void SetFailed_TransitionsCorrectly()
    {
        var job = CreateJob();
        job.SetRunning();
        job.SetFailed("编译失败");

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.NotNull(job.CompletedAt);
        Assert.Equal("编译失败", job.ErrorMessage);
        Assert.Contains("失败", job.StatusText);
    }

    [Fact]
    public void SetCancelled_TransitionsCorrectly()
    {
        var job = CreateJob();
        job.SetCancelled();

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Equal("用户取消", job.ErrorMessage);
        Assert.Contains("取消", job.StatusText);
    }

    [Fact]
    public void SetTimedOut_TransitionsCorrectly()
    {
        var job = CreateJob();
        job.SetRunning();
        job.SetTimedOut();

        Assert.Equal(JobStatus.TimedOut, job.Status);
        Assert.Contains("超时", job.ErrorMessage!);
        Assert.Contains("超时", job.StatusText);
    }

    [Fact]
    public void ClearResultBytes_NullifiesBytes()
    {
        var job = CreateJob();
        job.SetRunning();
        var bytes = new byte[] { 1, 2, 3 };
        var result = GenerateResult.Ok(bytes, "out.zip", TimeSpan.Zero);
        job.SetSucceeded(bytes, "out.zip", result);
        Assert.NotNull(job.ResultBytes);

        job.ClearResultBytes();
        Assert.Null(job.ResultBytes);
        // 但 ResultFileName 和 Result 仍保留
        Assert.Equal("out.zip", job.ResultFileName);
        Assert.NotNull(job.Result);
    }

    [Fact]
    public void StatusText_Queued_ShowsPosition()
    {
        var job = CreateJob();
        Assert.Contains("排队中", job.StatusText);
        Assert.Contains("1", job.StatusText);

        job.QueuePosition = 5;
        Assert.Contains("5", job.StatusText);
    }

    [Fact]
    public async Task EmitProgressEvent_PublishesToChannel()
    {
        var job = CreateJob();
        job.SetRunning();

        // 启动监听
        var events = new List<JobStatusEvent>();
        var watchTask = Task.Run(async () =>
        {
            await foreach (var e in job.WatchStatusAsync())
            {
                events.Add(e);
            }
        });

        // 发送进度事件
        await Task.Delay(50); // 让 watcher 先启动
        job.EmitProgressEvent("building", 50);
        job.EmitProgressEvent("packing", 80);

        // 完成后关闭通道
        var result = GenerateResult.Ok(Array.Empty<byte>(), "x.zip", TimeSpan.Zero);
        job.SetSucceeded(Array.Empty<byte>(), "x.zip", result);

        await watchTask.WaitAsync(TimeSpan.FromSeconds(5));

        // 应该有进度事件
        var buildingEvents = events.Where(e => e.Phase == "building").ToList();
        Assert.NotEmpty(buildingEvents);
        Assert.Contains(50, buildingEvents.Select(e => e.ProgressPercent));

        var packingEvents = events.Where(e => e.Phase == "packing").ToList();
        Assert.NotEmpty(packingEvents);
        Assert.Contains(80, packingEvents.Select(e => e.ProgressPercent));
    }

    [Fact]
    public async Task WatchStatusAsync_ReturnsInitialAndFinalEvents()
    {
        var job = CreateJob();

        // 短暂延迟确保初始事件已写入 Channel
        await Task.Delay(50);

        var events = new List<JobStatusEvent>();
        var watchTask = Task.Run(async () =>
        {
            await foreach (var e in job.WatchStatusAsync())
                events.Add(e);
        });

        job.SetRunning();
        var result = GenerateResult.Ok(Array.Empty<byte>(), "x.zip", TimeSpan.Zero);
        job.SetSucceeded(Array.Empty<byte>(), "x.zip", result);

        await watchTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotEmpty(events);
        Assert.Contains(events, e => e.Status == JobStatus.Queued);
        Assert.Contains(events, e => e.Status == JobStatus.Running);
        Assert.Contains(events, e => e.Status == JobStatus.Succeeded);
    }
}
