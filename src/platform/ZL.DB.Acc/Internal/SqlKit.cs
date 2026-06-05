using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZL.PFLite.Common;

namespace ZL.DB.Acc.Utils
{
    public class SqlKit
    {
        /// <summary>
        /// 只查询表头，不返回内容
        /// </summary>
        /// <param name="TableName"></param>
        /// <param name="fieldList"></param>
        /// <param name="QuerySql"></param>
        /// <returns></returns>
        public static string GenSelectSql(string TableName, string[] fieldList, string OrderBy = "", string QuerySql = "")
        {
            string sql = QuerySql;
            if (string.IsNullOrEmpty(sql) && !string.IsNullOrEmpty(TableName))
            {
                if (fieldList.Length == 0)
                {
                    sql = $"select * from {TableName} where 1<>1 ";
                }
                else
                {
                    string filedSelect = string.Join(", ", fieldList);
                    sql = $"select {filedSelect} from {TableName} where 1<>1 ";
                }
                sql += " " + OrderBy;
            }
            //else
            //    sql = ReplaceSqlVal(sql, keyList, rowVals);

            return sql;
        }
        public static string GenSelectSql(string TableName, string[] fieldList, string key, string value, string QueryType, string OrderBy = "", string QuerySql = "")
        {
            //构造查询条件
            //var conModels = new List<IConditionalModel>();
            string sql = QuerySql;
            if (string.IsNullOrEmpty(sql) && !string.IsNullOrEmpty(TableName))
            {
                if (fieldList.Length == 0)
                {
                    sql = $"select * from {TableName} where 1=1 ";
                }
                else
                {
                    string filedSelect = string.Join(", ", fieldList);
                    sql = $"select {filedSelect} from {TableName} where 1=1 ";
                }
            }
            //else
            //    sql = ReplaceSqlVal(sql, keyList, rowVals);

            string equal = " = ";
            if (key != null && value != null)
            {
                //查询条件不为null则增加查询条件
                //ConditionalType conditionalType = ConditionalType.Equal;
                switch (QueryType)
                {
                    case "E":
                        equal = $" = '{value}'";
                        //conditionalType = ConditionalType.Equal;
                        break;
                    case "L":
                        equal = $" like '%{value}%'";
                        //conditionalType = ConditionalType.Like;
                        break;
                    default:
                        equal = $" = '{value}'";
                        //conditionalType = ConditionalType.Equal;
                        break;
                }
                //conModels.Add(new ConditionalModel { FieldName = key, ConditionalType = conditionalType, FieldValue = value });
                if (!string.IsNullOrEmpty(key) || key != "null")
                    sql += $" And {key} {equal}";
            }
            sql += " " + OrderBy;
            return sql;
        }
        /// <summary>
        /// 获取字段在字段列表中的索引，用于获取值
        /// </summary>
        /// <param name="fieldList"></param>
        /// <param name="filed"></param>
        /// <returns></returns>
        public static int GetFieldIndex(string[] fieldList, string filed)
        {
            int index = -1;
            for (int i = 0; i < fieldList.Length; i++)
            {
                if (fieldList[i] == filed) { index = i; break; }
            }
            return index;
        }

        /// <summary>
        /// 生成insert语句
        /// </summary>
        /// <param name="TableName">表名</param>
        /// <param name="fieldList">列数组</param>
        /// <param name="valueList">值数组</param>
        /// <param name="pkField">主键字段(单)</param>
        /// <param name="pkFiledDataType">int 为自增长，string为guid</param>
        /// <param name="genPk">是否自动生成主键的值</param>
        /// <returns></returns>
        public static string GenInsertSql(string TableName, string[] fieldList, object[] valueList,
       string pkField = "ID", string pkFiledDataType = "int", bool genPk = true)
        {
            if (fieldList.Length != valueList.Length) return "";

            int pkValIndex = Array.FindIndex(fieldList, field => field.Equals(pkField, StringComparison.OrdinalIgnoreCase));

            if (genPk && pkValIndex >= 0)
            {
                //处理主键
                if (pkFiledDataType == "int")
                {
                    //删除原有id及值，使用数据库自增长字段的特性
                    RemoveAt(ref fieldList, pkValIndex);
                    RemoveAt(ref valueList, pkValIndex);
                }
                else if (pkFiledDataType == "string" && valueList[pkValIndex] == null)
                {
                    //主键就是 GUID
                    valueList[pkValIndex] = Guid.NewGuid().ToString();
                }
            }

            string columns = string.Join(", ", fieldList);
            string values = string.Join(", ", valueList.Select(v => FormatValueBasedOnType(v)));

            return $"INSERT INTO {TableName}({columns}) VALUES({values});";
        }

        private static void RemoveAt<T>(ref T[] array, int index)
        {
            T[] newArray = new T[array.Length - 1];
            Array.Copy(array, 0, newArray, 0, index);
            Array.Copy(array, index + 1, newArray, index, array.Length - index - 1);
            array = newArray;
        }

        private static string FormatValueBasedOnType(object value)
        {
            if (value == null) return "NULL";

            string type = value.GetType().Name;
            switch (type)
            {
                case "Byte":
                case "Int16":
                case "Int32":
                case "Int64":
                case "Double":
                case "Single":
                case "Decimal":
                    return value.ToString();
                case "Boolean":
                    return ((bool)value) ? "1" : "0"; // Convert Boolean to 1 or 0 for SQL
                default:
                    return $"'{value}'";
            }
        }

        public static string GenInsertSql2(string TableName, string[] fieldList, object[] valueList, string pkField = "ID", string pkFiledDataType = "int", bool genPk = true)
        {
            if (fieldList.Length != valueList.Length) return "";
            int pkValIndex = GetFieldIndex(fieldList, pkField);
            List<string> fields = new List<string>();
            List<object> vals = new List<object>();

            if (!genPk)
            {
                //主键不处理，数据原封不动的传递，包括主键
                fields = fieldList.ToList();
                vals = valueList.ToList();
            }
            else
            {
                if (fieldList.Contains(pkField))
                {
                    if (pkValIndex >= 0 && pkValIndex < fieldList.Length)
                    {
                        //处理主键
                        if (pkFiledDataType == "int")
                        {
                            //删除原有id及值，使用数据库自增长字段的特性
                            foreach (var it in fieldList)
                            {
                                //去除主键字段，形成新的数组
                                if (it.ToUpper() != pkField.ToUpper()) fields.Add(it);
                            }
                            for (int i = 0; i < valueList.Length; i++)
                            {
                                //去除主键的值，形成新的数组
                                if (i != pkValIndex) vals.Add(valueList[i]);
                            }
                        }
                        else if (pkFiledDataType == "string")
                        {
                            //主键就是 GUID
                            valueList[pkValIndex] = Guid.NewGuid().ToString();
                            fields = fieldList.ToList();
                            vals = valueList.ToList();
                        }
                    }
                }
                else
                {
                    fields = fieldList.ToList();
                    vals = valueList.ToList();
                }
            }

            string columns = string.Join(", ", fields);
            string values = string.Join("', '", vals);
            values = "'" + values + "'";
            //string pkVal = (pkFiledDataType == "string") ? Guid.NewGuid().ToString() : "";
            string sql = $"insert into {TableName}({columns}) values({values})";
            return sql;
        }

        /// <summary>
        /// 根据 TableName，字段、值及主键生成update sql
        /// </summary>
        public static string GenUpdateSql(string TableName, string[] fieldList, object[] valueList, string[] pkFields)
        {
            if (fieldList.Length != valueList.Length)
                throw new ArgumentException("fieldList and valueList should have the same length.");

            Dictionary<string, object> primaryKeys = pkFields.ToDictionary(pk => pk.ToUpper(), pk => (object)null);
            List<string> updates = new List<string>();

            for (int i = 0; i < fieldList.Length; i++)
            {
                var field = fieldList[i].ToUpper();
                if (primaryKeys.ContainsKey(field))
                {
                    primaryKeys[field] = valueList[i];
                    continue;
                }
                updates.Add($"{field}='{valueList[i]}'");
            }
            // 这里只检查是否为null，允许空字符串
            List<string> pkConditions = primaryKeys.Where(pk => pk.Value != null).Select(pk => $"{pk.Key}='{pk.Value}'").ToList();

            //if (pkConditions.Count != pkFields.Length)
            //    return "";  // 如果主键数量不匹配，则返回空字符串

            return $"UPDATE {TableName} SET {string.Join(", ", updates)} WHERE {string.Join(" AND ", pkConditions)}";
        }
        public static string GenUpdateSql2(string TableName, string[] fieldList, object[] valueList, string[] pkFields)
        {
            if (fieldList.Length != valueList.Length)
                throw new ArgumentException("fieldList and valueList should have the same length.");

            Dictionary<string, object> primaryKeys = pkFields.ToDictionary(pk => pk.ToUpper(), pk => (object)null);
            List<string> updates = new List<string>();

            for (int i = 0; i < fieldList.Length; i++)
            {
                var field = fieldList[i].ToUpper();
                if (valueList[i] == null)
                    valueList[i] = "";
                if (primaryKeys.ContainsKey(field))
                {
                    primaryKeys[field] = valueList[i];
                    continue;
                }
                updates.Add($"{field}='{valueList[i]}'");
            }

            List<string> pkConditions = primaryKeys.Select(pk => $"{pk.Key}='{pk.Value}'").ToList();
            if (pkConditions.Any(cond => string.IsNullOrEmpty(cond.Split('=')[1].Trim('\''))))
                return "";

            return $"UPDATE {TableName} SET {string.Join(", ", updates)} WHERE {string.Join(" AND ", pkConditions)}";
        }
        public static string GenDeleteSql(string TableName, string[] fieldList, object[] valueList, string[] pkFields)
        {
            if (fieldList.Length != valueList.Length)
                throw new ArgumentException("fieldList and valueList should have the same length.");

            Dictionary<string, object> primaryKeys = pkFields.ToDictionary(pk => pk.ToUpper(), pk => (object)null);

            for (int i = 0; i < fieldList.Length; i++)
            {
                var field = fieldList[i].ToUpper();
                if (primaryKeys.ContainsKey(field))
                {
                    primaryKeys[field] = valueList[i];
                }
            }

            List<string> pkConditions = primaryKeys.Select(pk => $"{pk.Key}='{pk.Value}'").ToList();
            if (pkConditions.Any(cond => string.IsNullOrEmpty(cond.Split('=')[1].Trim('\''))))
                return "";

            return $"DELETE FROM {TableName} WHERE {string.Join(" AND ", pkConditions)}";
        }

        public static string Replace(string sql, string key, string val)
        {
            //用于替换val
            if (sql.Contains("?" + key + "?"))
                sql = sql.Replace("?" + key + "?", val.Replace("'", "''"));
            if (sql.Contains("#" + key + "#"))
                sql = sql.Replace("#" + key + "#", val.Replace("'", "''"));
            if (sql.Contains("@" + key + "@"))
                sql = sql.Replace("@" + key + "@", val.Replace("'", "''"));
            return sql;
        }
        public static string ReplaceSqlVal(string sql, List<Tuple<string, string>> keyValList)
        {
            foreach (var it in keyValList)
            {
                var key = it.Item1;
                var val = it.Item2;
                sql = Replace(sql, key, val);
            }
            return sql;
        }
        public static string ReplaceSqlVal(string sql, string[] keyList, object[] valueList)
        {
            if (keyList.Length != valueList.Length)
                return sql;
            for (int i = 0; i < keyList.Length; i++)
            {
                var key = keyList[i];
                var val = valueList[i].ToString();
                sql = Replace(sql, key, val);
            }
            return sql;
        }
        public static string ReplaceSqlVal(string sql, Dictionary<string, string> valDic)
        {
            foreach (var it in valDic)
            {
                sql = Replace(sql, it.Key, it.Value);
            }
            return sql;
        }
    }
}
