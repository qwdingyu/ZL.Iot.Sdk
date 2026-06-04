using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace ZL.EdgeService
{
    /// <summary>
    /// 查询实体
    /// </summary>
    public class QueryEntity
    {
        /// <summary>
        /// 字段名称
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// 值
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// 操作方法，对应OperatorEnum枚举类
        /// </summary>
        public string Operator { get; set; }

        /// <summary>
        /// 逻辑运算符，只支持AND、OR
        /// </summary>
        public string LogicalOperator { get; set; }
    }

    /// <summary>
    /// 操作方法枚举
    /// </summary>
    public enum OperatorEnum
    {
        /// <summary>
        /// 等于
        /// </summary>
        Equals,

        /// <summary>
        /// 不等于
        /// </summary>
        NotEqual,

        /// <summary>
        /// 包含
        /// </summary>
        Contains,

        /// <summary>
        /// 由什么开始
        /// </summary>
        StartsWith,

        /// <summary>
        /// 由什么结束
        /// </summary>
        EndsWith,

        /// <summary>
        /// 大于
        /// </summary>
        Greater,

        /// <summary>
        /// 大于等于
        /// </summary>
        GreaterEqual,

        /// <summary>
        /// 小于
        /// </summary>
        Less,

        /// <summary>
        /// 小于等于
        /// </summary>
        LessEqual,
    }


    /// <summary>
    /// 表达式扩展
    /// </summary>
    /// <typeparam name="T">泛型</typeparam>
    public static class ExpressionExtension<T> where T : class, new()
    {
        /// <summary>
        /// 表达式动态拼接
        /// </summary>
        public static Expression<Func<T, bool>> ExpressionSplice(List<QueryEntity> entities)
        {
            if (entities.Count < 1)
            {
                return ex => true;
            }
            var expression_first = CreateExpressionDelegate(entities[0]);
            foreach (var entity in entities.Skip(1))
            {
                var expression = CreateExpressionDelegate(entity);
                InvocationExpression invocation = Expression.Invoke(expression_first, expression.Parameters.Cast<Expression>());
                BinaryExpression binary;
                // 逻辑运算符判断
                if (entity.LogicalOperator.ToUpper().Equals("OR"))
                {
                    binary = Expression.Or(expression.Body, invocation);
                }
                else
                {
                    binary = Expression.And(expression.Body, invocation);
                }
                expression_first = Expression.Lambda<Func<T, bool>>(binary, expression.Parameters);
            }
            return expression_first;
        }

        /// <summary>
        /// 创建 Expression<TDelegate>
        /// </summary>
        private static Expression<Func<T, bool>> CreateExpressionDelegate(QueryEntity entity)
        {
            ParameterExpression param = Expression.Parameter(typeof(T));

            Expression key = param;
            var entityKey = entity.Key.Trim();
            // 包含'.'，说明是父表的字段
            if (entityKey.Contains('.'))
            {
                var tableNameAndField = entityKey.Split('.');
                key = Expression.Property(key, tableNameAndField[0].ToString());
                key = Expression.Property(key, tableNameAndField[1].ToString());
            }
            else
            {
                key = Expression.Property(key, entityKey);
            }

            Expression value = Expression.Constant(ParseType(entity));
            Expression body = CreateExpression(key, value, entity.Operator);
            var lambda = Expression.Lambda<Func<T, bool>>(body, param);
            return lambda;
        }

        /// <summary>
        /// 属性类型转换
        /// </summary>
        /// <param name="entity">查询实体</param>
        /// <returns></returns>
        private static object ParseType(QueryEntity entity)
        {
            try
            {
                PropertyInfo property;
                // 包含'.'，说明是子类的字段
                if (entity.Key.Contains('.'))
                {
                    var tableNameAndField = entity.Key.Split('.');
                    property = typeof(T).GetProperty(tableNameAndField[0], BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                    property = property.PropertyType.GetProperty(tableNameAndField[1], BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                }
                else
                {
                    property = typeof(T).GetProperty(entity.Key, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                }
                return Convert.ChangeType(entity.Value, property.PropertyType);
            }
            catch (Exception)
            {
                throw new ArgumentException("字段类型转换失败：字段名错误或值类型不正确");
            }
        }

        /// <summary>
        /// 创建 Expression
        /// </summary>
        private static Expression CreateExpression(Expression left, Expression value, string entityOperator)
        {
            OperatorEnum operatorEnum;
            if (!Enum.TryParse(entityOperator, true, out operatorEnum))
            {
                throw new ArgumentException("操作方法不存在,请检查operator的值");
            }
            Expression exp;
            switch (operatorEnum)
            {
                case OperatorEnum.Equals:
                    exp = Expression.Equal(left, Expression.Convert(value, left.Type)); break;
                case OperatorEnum.NotEqual:
                    exp = Expression.NotEqual(left, Expression.Convert(value, left.Type)); break;
                case OperatorEnum.Contains:
                    exp = Expression.Call(left, typeof(string).GetMethod("Contains", new Type[] { typeof(string) }), value); break;
                case OperatorEnum.StartsWith:
                    exp = Expression.Call(left, typeof(string).GetMethod("StartsWith", new Type[] { typeof(string) }), value); break;
                case OperatorEnum.EndsWith:
                    exp = Expression.Call(left, typeof(string).GetMethod("EndsWith", new Type[] { typeof(string) }), value); break;
                case OperatorEnum.Greater:
                    exp = Expression.GreaterThan(left, Expression.Convert(value, left.Type)); break;
                case OperatorEnum.GreaterEqual:
                    exp = Expression.GreaterThanOrEqual(left, Expression.Convert(value, left.Type)); break;
                case OperatorEnum.Less:
                    exp = Expression.LessThan(left, Expression.Convert(value, left.Type)); break;
                case OperatorEnum.LessEqual:
                    exp = Expression.LessThanOrEqual(left, Expression.Convert(value, left.Type)); break;
                default: exp = Expression.Equal(left, Expression.Convert(value, left.Type)); break;
            }
            return exp;
        }
    }
    public class User
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Age { get; set; }

        public DateTime CreateTime { get; set; }

        public Address Address { get; set; }

    }

    public class Address
    {
        public string Province { get; set; }

        public string City { get; set; }
    }
    /// <summary>
    /// https://www.jb51.net/article/232915.htm
    /// </summary>
    public class TestExp
    {
        /// <summary>
        /// 查询用户表中名称(name) 包含 "chen" 并且年龄(age) 大于等于 18：
        /// </summary>
        public static void MultiQuery()
        {
            List<QueryEntity> list = new List<QueryEntity>
                {
                    new QueryEntity
                    {
                        Key = "name",
                        Value = "chen",
                        Operator = "Contains"
                    },
                    new QueryEntity
                    {
                        Key = "age",
                        Value = "18",
                        Operator = "GreaterEqual",
                        LogicalOperator= "AND"
                        // 注意：这里得填入 "AND",代表两个条件是并且的关系，如果需要查询名称包含 "chen" 或者 年龄大于等于18，则填入 "OR"
                    }
                 };
            var expression = ExpressionExtension<User>.ExpressionSplice(list);
            // expression = Param_0 => ((Param_0.Status >= Convert(1, Int32)) And Invoke(Param_1 => Param_1.OpenId.Contains("9JJdFTVt6oimCgdbW61sk"), Param_0))
        }
        public static void MultiTableQuery()
        {
            List<QueryEntity> list = new List<QueryEntity>
                {
                    new QueryEntity
                    {
                        Key = "name",
                        Value = "chen",
                        Operator = "Contains"
                    },
                    new QueryEntity
                    {
                        Key = "address.Province",
                        Value = "广东省",
                        Operator = "Equals",
                        LogicalOperator = "AND"
                        // 注意：这里得填入 "AND",代表两个条件是并且的关系，如果需要查询名称包含 "chen" 或者 年龄大于等于18，则填入 "OR"
                    }
                };

            //var expression = ExpressionExtension<BookingRecord>.ExpressionSplice(list);
            // expression = {Param_0 => ((Param_0.Address.Province == Convert("广东省", String)) And Invoke(Param_1 => Param_1.Name.Contains("chen"), Param_0))}
        }
    }
}
