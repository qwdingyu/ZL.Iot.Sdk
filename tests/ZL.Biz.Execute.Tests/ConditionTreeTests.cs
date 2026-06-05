using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ZL.Biz.Execute.Conditions;
using ZL.Biz.Execute.Actions;

namespace ZL.Biz.Execute.Tests
{
    /// <summary>
    /// 条件树求值器测试
    /// </summary>
    public class ConditionTreeTests
    {
        private readonly ConditionTreeEvaluator _evaluator;

        public ConditionTreeTests()
        {
            var loggerMock = new Mock<ILogger<ConditionTreeEvaluator>>();
            _evaluator = new ConditionTreeEvaluator(loggerMock.Object);
        }

        [Fact]
        public void Test_EqualsCondition_True()
        {
            var condition = ConditionTree.Equals("status", 1);
            var facts = new Dictionary<string, object> { { "status", 1 } };
            Assert.True(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_EqualsCondition_False()
        {
            var condition = ConditionTree.Equals("status", 1);
            var facts = new Dictionary<string, object> { { "status", 0 } };
            Assert.False(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_GreaterThanCondition()
        {
            var condition = ConditionTree.GreaterThan("value", 100);
            var facts = new Dictionary<string, object> { { "value", 150 } };
            Assert.True(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_LessThanCondition()
        {
            var condition = ConditionTree.LessThan("value", 100);
            var facts = new Dictionary<string, object> { { "value", 50 } };
            Assert.True(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_AndCondition_AllTrue()
        {
            var condition = ConditionTree.And(
                ConditionTree.Equals("a", 1),
                ConditionTree.Equals("b", 2)
            );
            var facts = new Dictionary<string, object> { { "a", 1 }, { "b", 2 } };
            Assert.True(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_AndCondition_OneFalse()
        {
            var condition = ConditionTree.And(
                ConditionTree.Equals("a", 1),
                ConditionTree.Equals("b", 2)
            );
            var facts = new Dictionary<string, object> { { "a", 1 }, { "b", 3 } };
            Assert.False(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_OrCondition_OneTrue()
        {
            var condition = ConditionTree.Or(
                ConditionTree.Equals("a", 1),
                ConditionTree.Equals("b", 2)
            );
            var facts = new Dictionary<string, object> { { "a", 1 }, { "b", 3 } };
            Assert.True(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_OrCondition_AllFalse()
        {
            var condition = ConditionTree.Or(
                ConditionTree.Equals("a", 1),
                ConditionTree.Equals("b", 2)
            );
            var facts = new Dictionary<string, object> { { "a", 0 }, { "b", 3 } };
            Assert.False(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_NotCondition()
        {
            var condition = ConditionTree.Not(ConditionTree.Equals("flag", true));
            var facts = new Dictionary<string, object> { { "flag", false } };
            Assert.True(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_ComplexNestedCondition()
        {
            // (a > 10 AND b = "test") OR (c IS NULL)
            var condition = ConditionTree.Or(
                ConditionTree.And(
                    ConditionTree.GreaterThan("a", 10),
                    ConditionTree.Equals("b", "test")
                ),
                ConditionTree.IsNull("c")
            );
            var facts = new Dictionary<string, object>
            {
                { "a", 15 },
                { "b", "test" },
                { "c", "value" }
            };
            Assert.True(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_ContainsCondition()
        {
            var condition = ConditionTree.Contains("name", "test");
            var facts = new Dictionary<string, object> { { "name", "this is a test string" } };
            Assert.True(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_StartsWithCondition()
        {
            var condition = ConditionTree.StartsWith("name", "prefix");
            var facts = new Dictionary<string, object> { { "name", "prefix_suffix" } };
            Assert.True(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_EndsWithCondition()
        {
            var condition = ConditionTree.EndsWith("name", "suffix");
            var facts = new Dictionary<string, object> { { "name", "prefix_suffix" } };
            Assert.True(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_InCondition()
        {
            var condition = ConditionTree.In("status", new object[] { 1, 2, 3 });
            var facts = new Dictionary<string, object> { { "status", 2 } };
            Assert.True(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_InCondition_NotIn()
        {
            var condition = ConditionTree.In("status", new object[] { 1, 2, 3 });
            var facts = new Dictionary<string, object> { { "status", 5 } };
            Assert.False(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_BetweenCondition()
        {
            var condition = ConditionTree.Between("value", 10, 20);
            var facts = new Dictionary<string, object> { { "value", 15 } };
            Assert.True(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_BetweenCondition_OutOfRange()
        {
            var condition = ConditionTree.Between("value", 10, 20);
            var facts = new Dictionary<string, object> { { "value", 25 } };
            Assert.False(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_IsNullCondition()
        {
            var condition = ConditionTree.IsNull("missing");
            var facts = new Dictionary<string, object> { { "other", "value" } };
            Assert.True(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_IsNotNullCondition()
        {
            var condition = ConditionTree.IsNotNull("existing");
            var facts = new Dictionary<string, object> { { "existing", "value" } };
            Assert.True(_evaluator.Evaluate(condition, facts));
        }

        [Fact]
        public void Test_StringComparison()
        {
            var condition = ConditionTree.Equals("name", "test");
            var facts = new Dictionary<string, object> { { "name", "test" } };
            Assert.True(_evaluator.Evaluate(condition, facts));
        }
    }

    /// <summary>
    /// 强类型动作执行器测试
    /// </summary>
    public class TypedActionTests
    {
        [Fact]
        public void Test_InsertRowActionExecutor_ValidateParameters_Missing()
        {
            var loggerMock = new Mock<ILogger<InsertRowActionExecutor>>();
            var executor = new InsertRowActionExecutor(loggerMock.Object);

            var result = executor.ValidateParameters(new Dictionary<string, object>(), out var error);
            Assert.False(result);
            Assert.NotNull(error);
        }

        [Fact]
        public void Test_InsertRowActionExecutor_ValidateParameters_Valid()
        {
            var loggerMock = new Mock<ILogger<InsertRowActionExecutor>>();
            var executor = new InsertRowActionExecutor(loggerMock.Object);

            var params_ = new Dictionary<string, object>
            {
                { "tableName", "test" },
                { "data", new Dictionary<string, object>() }
            };
            var result = executor.ValidateParameters(params_, out var error);
            Assert.True(result);
            Assert.Null(error);
        }

        [Fact]
        public void Test_UpdateRowActionExecutor_ValidateParameters_Valid()
        {
            var loggerMock = new Mock<ILogger<UpdateRowActionExecutor>>();
            var executor = new UpdateRowActionExecutor(loggerMock.Object);

            var params_ = new Dictionary<string, object>
            {
                { "tableName", "test" },
                { "primaryKey", "id" },
                { "primaryKeyValue", 1 },
                { "data", new Dictionary<string, object> { { "name", "test" } } }
            };
            var result = executor.ValidateParameters(params_, out var error);
            Assert.True(result);
            Assert.Null(error);
        }

        [Fact]
        public void Test_WriteTagActionExecutor_ValidateParameters_Valid()
        {
            var loggerMock = new Mock<ILogger<WriteTagActionExecutor>>();
            var executor = new WriteTagActionExecutor(loggerMock.Object);

            var params_ = new Dictionary<string, object>
            {
                { "tagId", "tag001" },
                { "value", 100 }
            };
            var result = executor.ValidateParameters(params_, out var error);
            Assert.True(result);
            Assert.Null(error);
        }

        [Fact]
        public void Test_TypedActionRegistry_RegisterAndGet()
        {
            var registry = new TypedActionRegistry();
            var loggerMock = new Mock<ILogger<InsertRowActionExecutor>>();
            var executor = new InsertRowActionExecutor(loggerMock.Object);
            
            registry.Register(executor);
            var retrieved = registry.GetExecutor("InsertRow");
            
            Assert.NotNull(retrieved);
            Assert.Equal("InsertRow", retrieved.ActionType);
        }
    }
}