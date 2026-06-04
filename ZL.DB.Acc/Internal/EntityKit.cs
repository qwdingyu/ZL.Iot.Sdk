using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;

namespace ZL.DB.Acc.Utils
{
    /// <summary>
    /// 将DataTable数据源转换成实体类
    /// </summary>
    /// <typeparam name="T">实体</typeparam>

    public class EntityKit
    {
        /// <summary>
        /// 将DataTable数据源转换成实体类
        /// </summary>             
        public static List<T> ConvertToEntity<T>(DataTable dt) where T : new()
        {
            List<T> ts = new List<T>();// 定义集合
            foreach (DataRow dr in dt.Rows)
            {
                T t = ConvertToEntity<T>(dr);
                ts.Add(t);
            }
            return ts;
        }
        /// <summary>
        /// dataRow转换成实体对象
        /// </summary>
        public static T ConvertToEntity<T>(DataRow dr) where T : new()
        {

            if (dr == null)
            {
                return default(T);
            }
            T t = new T();
            //忽略大小写的方法，比如数据库字段都是大写，但是实体类是C#骆驼峰式或其他写法时不匹配。
            //PropertyInfo[] propertyInfos = t.GetType().GetProperties(dr.Table.Columns[i].ColumnName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo[] propertys = t.GetType().GetProperties();// 获得此模型的公共属性
            foreach (PropertyInfo pi in propertys)
            {
                if (dr.Table.Columns.Contains(pi.Name))
                {
                    if (!pi.CanWrite) continue;
                    var value = dr[pi.Name];
                    SetVal(pi, value);
                    if (value != DBNull.Value)
                    {
                        switch (pi.PropertyType.FullName)
                        {
                            case "System.Decimal":
                                pi.SetValue(t, decimal.Parse(value.ToString()), null);
                                break;
                            case "System.String":
                                pi.SetValue(t, value.ToString(), null);
                                break;
                            case "System.Int32":
                                pi.SetValue(t, int.Parse(value.ToString()), null);
                                break;
                            case "System.Byte":
                                pi.SetValue(t, Byte.Parse(value.ToString()), null);
                                break;

                            default:
                                pi.SetValue(t, value, null);
                                break;
                        }
                    }
                }
            }
            return t;
        }
        public static T ConvertToEntity<T>(DataTable dataTable, int index) where T : new()
        {
            T t = new T();
            DataRow dataRow = dataTable.Rows[index];
            return ConvertToEntity<T>(dataRow);
        }

        private static PropertyInfo SetVal(PropertyInfo pi, object value)
        {
            return pi;
        }
    }
}
