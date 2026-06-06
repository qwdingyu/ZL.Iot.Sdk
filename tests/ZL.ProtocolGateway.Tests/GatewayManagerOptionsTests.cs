// ============================================================
// 文件：GatewayManagerOptionsTests.cs
// 描述：GatewayManagerOptions 配置校验单元测试
// 修改日期：2026-06-03
// ============================================================

using System;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class GatewayManagerOptionsTests
    {
        [Fact]
        public void Validate_DefaultValues_Passes()
        {
            var options = new GatewayManagerOptions();
            var errors = options.Validate();
            Assert.Empty(errors);
        }

        [Fact]
        public void Validate_AllValidCustomValues_Passes()
        {
            var options = new GatewayManagerOptions
            {
                QueueCapacity = 50000,
                SendTimeoutMs = 60000,
                MaxRetryAttempts = 5,
                RetryBaseDelayMs = 200,
                CircuitBreakerFailureThreshold = 10,
                CircuitBreakerRecoveryTimeMs = 120000
            };
            var errors = options.Validate();
            Assert.Empty(errors);
        }

        [Fact]
        public void Validate_MinimumValidValues_Passes()
        {
            var options = new GatewayManagerOptions
            {
                QueueCapacity = 100,
                SendTimeoutMs = 1000,
                MaxRetryAttempts = 0,
                RetryBaseDelayMs = 10,
                CircuitBreakerFailureThreshold = 1,
                CircuitBreakerRecoveryTimeMs = 1000
            };
            var errors = options.Validate();
            Assert.Empty(errors);
        }

        [Fact]
        public void Validate_QueueCapacityTooLow_ReturnsError()
        {
            var options = new GatewayManagerOptions { QueueCapacity = 50 };
            var errors = options.Validate();
            Assert.Single(errors);
            Assert.Equal(nameof(GatewayManagerOptions.QueueCapacity), errors[0].PropertyName);
            Assert.Contains("最小 100", errors[0].ErrorMessage);
        }

        [Fact]
        public void Validate_SendTimeoutTooLow_ReturnsError()
        {
            var options = new GatewayManagerOptions { SendTimeoutMs = 500 };
            var errors = options.Validate();
            Assert.Single(errors);
            Assert.Equal(nameof(GatewayManagerOptions.SendTimeoutMs), errors[0].PropertyName);
        }

        [Fact]
        public void Validate_SendTimeoutZero_ReturnsError()
        {
            var options = new GatewayManagerOptions { SendTimeoutMs = 0 };
            var errors = options.Validate();
            Assert.Single(errors);
            Assert.Equal(nameof(GatewayManagerOptions.SendTimeoutMs), errors[0].PropertyName);
        }

        [Fact]
        public void Validate_NegativeRetryAttempts_ReturnsError()
        {
            var options = new GatewayManagerOptions { MaxRetryAttempts = -1 };
            var errors = options.Validate();
            Assert.Single(errors);
            Assert.Equal(nameof(GatewayManagerOptions.MaxRetryAttempts), errors[0].PropertyName);
        }

        [Fact]
        public void Validate_RetryBaseDelayTooLow_ReturnsError()
        {
            var options = new GatewayManagerOptions { RetryBaseDelayMs = 5 };
            var errors = options.Validate();
            Assert.Single(errors);
            Assert.Equal(nameof(GatewayManagerOptions.RetryBaseDelayMs), errors[0].PropertyName);
        }

        [Fact]
        public void Validate_CircuitBreakerThresholdTooLow_ReturnsError()
        {
            var options = new GatewayManagerOptions { CircuitBreakerFailureThreshold = 0 };
            var errors = options.Validate();
            Assert.Single(errors);
            Assert.Equal(nameof(GatewayManagerOptions.CircuitBreakerFailureThreshold), errors[0].PropertyName);
        }

        [Fact]
        public void Validate_CircuitBreakerRecoveryTooLow_ReturnsError()
        {
            var options = new GatewayManagerOptions { CircuitBreakerRecoveryTimeMs = 500 };
            var errors = options.Validate();
            Assert.Single(errors);
            Assert.Equal(nameof(GatewayManagerOptions.CircuitBreakerRecoveryTimeMs), errors[0].PropertyName);
        }

        [Fact]
        public void Validate_AllInvalid_ReturnsAllErrors()
        {
            var options = new GatewayManagerOptions
            {
                QueueCapacity = 1,
                SendTimeoutMs = 0,
                MaxRetryAttempts = -1,
                RetryBaseDelayMs = 1,
                CircuitBreakerFailureThreshold = 0,
                CircuitBreakerRecoveryTimeMs = 0
            };
            var errors = options.Validate();
            Assert.Equal(6, errors.Count);

            var propertyNames = new System.Collections.Generic.List<string>();
            foreach (var e in errors) propertyNames.Add(e.PropertyName);

            Assert.Contains(nameof(GatewayManagerOptions.QueueCapacity), propertyNames);
            Assert.Contains(nameof(GatewayManagerOptions.SendTimeoutMs), propertyNames);
            Assert.Contains(nameof(GatewayManagerOptions.MaxRetryAttempts), propertyNames);
            Assert.Contains(nameof(GatewayManagerOptions.RetryBaseDelayMs), propertyNames);
            Assert.Contains(nameof(GatewayManagerOptions.CircuitBreakerFailureThreshold), propertyNames);
            Assert.Contains(nameof(GatewayManagerOptions.CircuitBreakerRecoveryTimeMs), propertyNames);
        }
    }
}
