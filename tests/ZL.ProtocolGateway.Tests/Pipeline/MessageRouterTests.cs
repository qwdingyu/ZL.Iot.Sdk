using System.Collections.Generic;
using Xunit;

namespace ZL.ProtocolGateway.Tests.Pipeline
{
    /// <summary>
    /// MessageRouter 测试 — 验证路由匹配、优先级排序、短路匹配。
    /// </summary>
    public class MessageRouterTests
    {
        [Fact]
        public void Route_NoRules_ReturnsEmpty()
        {
            var router = new MessageRouter();
            var msg = new Message { Topic = "test" };

            var result = router.Route(msg);

            Assert.Empty(result);
        }

        [Fact]
        public void Route_MatchingRule_ReturnsOutputNames()
        {
            var router = new MessageRouter();
            router.AddRule(new RouteRule
            {
                Condition = m => m.Topic.StartsWith("sensor/"),
                OutputNames = new List<string> { "MqttOut", "FileOut" }
            });

            var msg = new Message { Topic = "sensor/temp" };
            var result = router.Route(msg);

            Assert.Contains("MqttOut", result);
            Assert.Contains("FileOut", result);
        }

        [Fact]
        public void Route_NonMatchingRule_ReturnsEmpty()
        {
            var router = new MessageRouter();
            router.AddRule(new RouteRule
            {
                Condition = m => m.Topic.StartsWith("sensor/"),
                OutputNames = new List<string> { "MqttOut" }
            });

            var msg = new Message { Topic = "alarm/fire" };
            var result = router.Route(msg);

            Assert.Empty(result);
        }

        [Fact]
        public void Route_ShortCircuit_StopsAtFirstMatch()
        {
            var router = new MessageRouter();
            router.AddRule(new RouteRule
            {
                Condition = m => m.Topic.StartsWith("sensor/"),
                OutputNames = new List<string> { "FirstOut" },
                ContinueMatching = false // 短路
            });
            router.AddRule(new RouteRule
            {
                Condition = m => true,
                OutputNames = new List<string> { "CatchAllOut" }
            });

            var msg = new Message { Topic = "sensor/temp" };
            var result = router.Route(msg);

            Assert.Single(result);
            Assert.Contains("FirstOut", result);
            Assert.DoesNotContain("CatchAllOut", result);
        }

        [Fact]
        public void Route_ContinueMatching_MergesOutputs()
        {
            var router = new MessageRouter();
            router.AddRule(new RouteRule
            {
                Condition = m => m.Topic.StartsWith("sensor/"),
                OutputNames = new List<string> { "MqttOut" },
                ContinueMatching = true
            });
            router.AddRule(new RouteRule
            {
                Condition = m => m.Topic.Contains("temp"),
                OutputNames = new List<string> { "InfluxOut" },
                ContinueMatching = true
            });

            var msg = new Message { Topic = "sensor/temp" };
            var result = router.Route(msg);

            Assert.Equal(2, result.Count);
            Assert.Contains("MqttOut", result);
            Assert.Contains("InfluxOut", result);
        }

        [Fact]
        public void Route_HigherPriorityRuleMatchesFirst()
        {
            var router = new MessageRouter();
            // 先添加低优先级规则
            router.AddRule(new RouteRule
            {
                Name = "LowPriority",
                Condition = m => m.Topic.StartsWith("data/"),
                OutputNames = new List<string> { "DefaultOut" },
                Priority = 200
            });
            // 再添加高优先级规则
            router.AddRule(new RouteRule
            {
                Name = "HighPriority",
                Condition = m => m.Topic.StartsWith("data/critical/"),
                OutputNames = new List<string> { "CriticalOut" },
                Priority = 10,
                ContinueMatching = false
            });

            var msg = new Message { Topic = "data/critical/alarm" };
            var result = router.Route(msg);

            // 高优先级规则短路，不应匹配低优先级
            Assert.Single(result);
            Assert.Contains("CriticalOut", result);
        }

        [Fact]
        public void Route_NonShortCircuit_MatchesAllRules()
        {
            var router = new MessageRouter();
            router.AddRule(new RouteRule
            {
                Condition = m => m.Topic.StartsWith("data/"),
                OutputNames = new List<string> { "DefaultOut" },
                Priority = 200
            });
            router.AddRule(new RouteRule
            {
                Condition = m => m.Topic.StartsWith("data/critical/"),
                OutputNames = new List<string> { "CriticalOut" },
                Priority = 10,
                ContinueMatching = true // 不短路
            });

            var msg = new Message { Topic = "data/critical/alarm" };
            var result = router.Route(msg);

            Assert.Equal(2, result.Count);
            Assert.Contains("CriticalOut", result);
            Assert.Contains("DefaultOut", result);
        }

        [Fact]
        public void RuleCount_ReturnsCorrectCount()
        {
            var router = new MessageRouter();
            Assert.Equal(0, router.RuleCount);

            router.AddRule(new RouteRule { Condition = m => true, OutputNames = new List<string> { "A" } });
            Assert.Equal(1, router.RuleCount);

            router.AddRule(new RouteRule { Condition = m => true, OutputNames = new List<string> { "B" } });
            Assert.Equal(2, router.RuleCount);
        }

        [Fact]
        public void Route_DeduplicatesOutputNames()
        {
            var router = new MessageRouter();
            router.AddRule(new RouteRule
            {
                Condition = m => true,
                OutputNames = new List<string> { "SharedOut" }
            });
            router.AddRule(new RouteRule
            {
                Condition = m => true,
                OutputNames = new List<string> { "SharedOut" } // 重复
            });

            var msg = new Message { Topic = "any" };
            var result = router.Route(msg);

            Assert.Single(result);
        }

        [Fact]
        public void Route_NullCondition_ReturnsFalse()
        {
            var router = new MessageRouter();
            router.AddRule(new RouteRule
            {
                Condition = null!,
                OutputNames = new List<string> { "ShouldNotMatch" }
            });

            var msg = new Message { Topic = "test" };
            var result = router.Route(msg);

            Assert.Empty(result);
        }
    }
}
