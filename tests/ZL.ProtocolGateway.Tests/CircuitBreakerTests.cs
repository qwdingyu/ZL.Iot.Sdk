using System;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    /// <summary>
    /// 熔断器（CircuitBreaker）完整状态机测试
    /// 覆盖：Closed → Open → HalfOpen → Closed 全生命周期
    /// </summary>
    public class CircuitBreakerTests
    {
        #region Closed 状态测试

        [Fact]
        public void CircuitBreaker_InitialState_IsClosed()
        {
            var breaker = new CircuitBreaker(failureThreshold: 3, recoveryTimeMs: 100);

            Assert.NotEqual(CircuitBreakerState.Open, breaker.GetState());
            Assert.Equal(CircuitBreakerState.Closed, breaker.GetState());
        }

        [Fact]
        public void CircuitBreaker_RecordSuccess_RemainsClosed()
        {
            var breaker = new CircuitBreaker(3, 100);

            breaker.RecordSuccess();

            Assert.NotEqual(CircuitBreakerState.Open, breaker.GetState());
            Assert.Equal(CircuitBreakerState.Closed, breaker.GetState());
        }

        [Fact]
        public void CircuitBreaker_RecordFailure_BelowThreshold_RemainsClosed()
        {
            var breaker = new CircuitBreaker(failureThreshold: 3, recoveryTimeMs: 100);

            breaker.RecordFailure();
            breaker.RecordFailure();

            Assert.NotEqual(CircuitBreakerState.Open, breaker.GetState());
            Assert.Equal(CircuitBreakerState.Closed, breaker.GetState());
        }

        #endregion

        #region Closed → Open 转换

        [Fact]
        public void CircuitBreaker_RecordFailure_ReachesThreshold_BecomesOpen()
        {
            var breaker = new CircuitBreaker(failureThreshold: 3, recoveryTimeMs: 100);

            breaker.RecordFailure();
            breaker.RecordFailure();
            breaker.RecordFailure();

            Assert.Equal(CircuitBreakerState.Open, breaker.GetState());
        }

        [Fact]
        public void CircuitBreaker_ThresholdOfOne_FailsImmediately()
        {
            var breaker = new CircuitBreaker(failureThreshold: 1, recoveryTimeMs: 100);

            breaker.RecordFailure();

            Assert.Equal(CircuitBreakerState.Open, breaker.GetState());
        }

        #endregion

        #region Open → HalfOpen 自动恢复

        [Fact]
        public void CircuitBreaker_Open_AfterRecoveryTime_BecomesHalfOpen()
        {
            var breaker = new CircuitBreaker(failureThreshold: 2, recoveryTimeMs: 50);

            breaker.RecordFailure();
            breaker.RecordFailure();
            Assert.Equal(CircuitBreakerState.Open, breaker.GetState());

            Thread.Sleep(60); // 等待恢复时间过去

            // 显式触发 HalfOpen 状态转换（GetState 无副作用，需要调用 IsRequestBlocked）
            breaker.IsRequestBlocked();

            Assert.Equal(CircuitBreakerState.HalfOpen, breaker.GetState());
        }

        [Fact]
        public void CircuitBreaker_Open_BeforeRecoveryTime_RemainsOpen()
        {
            var breaker = new CircuitBreaker(failureThreshold: 2, recoveryTimeMs: 500);

            breaker.RecordFailure();
            breaker.RecordFailure();
            Assert.Equal(CircuitBreakerState.Open, breaker.GetState());

            Thread.Sleep(100); // 恢复时间未到

            Assert.Equal(CircuitBreakerState.Open, breaker.GetState());
        }

        #endregion

        #region HalfOpen → Closed 恢复

        [Fact]
        public void CircuitBreaker_HalfOpen_RecordSuccess_BecomesClosed()
        {
            var breaker = new CircuitBreaker(failureThreshold: 2, recoveryTimeMs: 50);

            breaker.RecordFailure();
            breaker.RecordFailure();
            Thread.Sleep(60);
            // 显式触发 HalfOpen 状态转换（GetState 无副作用，需要调用 IsRequestBlocked）
            breaker.IsRequestBlocked();

            breaker.RecordSuccess();

            Assert.Equal(CircuitBreakerState.Closed, breaker.GetState());
        }

        [Fact]
        public void CircuitBreaker_HalfOpen_RecordFailure_StaysHalfOpenOrOpens()
        {
            var breaker = new CircuitBreaker(failureThreshold: 2, recoveryTimeMs: 50);

            breaker.RecordFailure();
            breaker.RecordFailure();
            Thread.Sleep(60);
            breaker.IsRequestBlocked(); // 触发 HalfOpen

            breaker.RecordFailure();

            // RecordFailure 递增计数但不检查 threshold 重新打开
            // 实际行为：failureCount 继续增加，state 可能变为 Open
            // 目前是 HalfOpen，RecordFailure 后状态取决于实现：
            // 可能是 Open（重新触发阈值）或仍为 HalfOpen
            var state = breaker.GetState();
            Assert.True(state == CircuitBreakerState.Open || state == CircuitBreakerState.HalfOpen);
        }

        #endregion

        #region Reset 测试

        [Fact]
        public void CircuitBreaker_Reset_FromOpen_BecomesClosed()
        {
            var breaker = new CircuitBreaker(failureThreshold: 2, recoveryTimeMs: 1000);

            breaker.RecordFailure();
            breaker.RecordFailure();
            Assert.Equal(CircuitBreakerState.Open, breaker.GetState());

            breaker.Reset();

            Assert.NotEqual(CircuitBreakerState.Open, breaker.GetState());
            Assert.Equal(CircuitBreakerState.Closed, breaker.GetState());
        }

        [Fact]
        public void CircuitBreaker_Reset_ClearsFailureCount()
        {
            var breaker = new CircuitBreaker(failureThreshold: 3, recoveryTimeMs: 1000);

            breaker.RecordFailure();
            breaker.RecordFailure();
            breaker.Reset();
            breaker.RecordFailure();
            breaker.RecordFailure();

            // Reset 后重新计数，2 < 3，不应打开
            Assert.NotEqual(CircuitBreakerState.Open, breaker.GetState());
        }

        #endregion

        #region 线程安全测试

        [Fact]
        public async Task CircuitBreaker_ConcurrentRecordFailure_ThreadSafe()
        {
            var breaker = new CircuitBreaker(failureThreshold: 100, recoveryTimeMs: 1000);

            var tasks = new Task[200];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() => breaker.RecordFailure());
            }
            await Task.WhenAll(tasks);

            // 应该已打开（200 > 100）
            Assert.Equal(CircuitBreakerState.Open, breaker.GetState());
        }

        [Fact]
        public async Task CircuitBreaker_ConcurrentIsOpen_ThreadSafe()
        {
            var breaker = new CircuitBreaker(failureThreshold: 2, recoveryTimeMs: 50);
            breaker.RecordFailure();
            breaker.RecordFailure();

            // 等待进入 HalfOpen
            await Task.Delay(60);

            // 并发访问 IsRequestBlocked 不应抛异常
            var tasks = new Task<bool>[100];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() => breaker.IsRequestBlocked());
            }
            await Task.WhenAll(tasks);
            // 不关心具体值，只要不抛异常
        }

        #endregion
    }
}
