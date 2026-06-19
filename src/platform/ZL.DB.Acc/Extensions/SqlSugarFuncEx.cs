using System;
using System.Collections.Generic;
using SqlSugar;

namespace ZL.DB.Acc
{
    /// <summary>
    /// SqlSugar扩展函数
    /// </summary>
    public static class SqlSugarFuncEx
    {
        public static void SetExpMethods(this SqlSugarScope sss)
        {
            //Create ext method
            var expMethods = new List<SqlFuncExternal>();
            expMethods.Add(new SqlFuncExternal()
            {
                UniqueMethodName = "MyToString",
                MethodValue = (expInfo, dbType, expContext) =>
                {
                    object MemberName1 = expInfo.Args[0].MemberName;
                    if (dbType == DbType.SqlServer)
                        return $"CAST({MemberName1} AS VARCHAR(MAX))";
                    else
                        throw new Exception("未实现");
                }
            });
            expMethods.Add(new SqlFuncExternal()
            {
                UniqueMethodName = "ConvertTime",
                MethodValue = (expInfo, dbType, expContext) =>
                {
                    object MemberName1 = expInfo.Args[0].MemberName;
                    if (dbType == SqlSugar.DbType.MySql)
                        return $"from_unixtime({MemberName1})";
                    else
                        throw new Exception("未实现");
                }
            });
            expMethods.Add(new SqlFuncExternal()
            {
                UniqueMethodName = "UNIX_TIMESTAMP",
                MethodValue = (expInfo, dbType, expContext) =>
                {
                    object MemberName1 = expInfo.Args[0].MemberName;
                    if (dbType == SqlSugar.DbType.MySql)
                        return $"UNIX_TIMESTAMP({MemberName1})";
                    else
                        throw new Exception("未实现");
                }
            });
            expMethods.Add(new SqlFuncExternal
            {
                UniqueMethodName = "GroupConcat",
                MethodValue = (expInfo, dbType, expContext) =>
                {
                    object MemberName1 = expInfo.Args[0].MemberName;
                    if (dbType == DbType.MySql)
                    {
                        return $@"group_concat( {MemberName1} SEPARATOR ',' ) ";
                    }
                    else
                    {
                        throw new Exception("未实现");
                    }
                }
            });
            expMethods.Add(new SqlFuncExternal()
            {
                UniqueMethodName = "PartitionDateDiff",
                MethodValue = (expInfo, dbType, expContext) =>
                {
                    object MemberName1 = expInfo.Args[0].MemberName;
                    object MemberName2 = expInfo.Args[1].MemberName;
                    if (dbType == DbType.MySql)
                    {
                        return $"*,ROW_NUMBER() over(partition by {MemberName1} order by abs(datediff({MemberName2}, NOW() )) )";
                    }
                    else
                    {
                        throw new Exception("未实现");
                    }
                }
            });
            expMethods.Add(new SqlFuncExternal()
            {
                UniqueMethodName = "GetDuration",
                MethodValue = (expInfo, dbType, expContext) =>
                {
                    //string interval = "SECOND";
                    //string end = "";
                    string _end = string.Empty;
                    string start = expInfo.Args[0].MemberName.ToString();
                    string interval = expInfo.Args[1].MemberName.ToString();
                    string end = expInfo.Args[2].MemberName.ToString();
                    if (dbType == DbType.Oracle)
                    {
                        _end = (string.IsNullOrEmpty(end)) ? "SYSDATE" : end;
                        // 未实现
                        return "SYSDATE";
                    }
                    else if (dbType == DbType.SqlServer)
                    {
                        _end = (string.IsNullOrEmpty(end)) ? "GETDATE()" : end;
                        return $"DATEDIFF({interval}, {start}, {_end})";
                    }
                    else if (dbType == DbType.MySql)
                    {
                        //# 所有格式
                        //SELECT TIMESTAMPDIFF(FRAC_SECOND,'2012-10-01', '2013-01-13'); # 暂不支持
                        //SELECT TIMESTAMPDIFF(SECOND,'2012-10-01', '2013-01-13'); # 8985600
                        //SELECT TIMESTAMPDIFF(MINUTE,'2012-10-01', '2013-01-13'); # 149760
                        //SELECT TIMESTAMPDIFF(HOUR,'2012-10-01', '2013-01-13'); # 2496
                        //SELECT TIMESTAMPDIFF(DAY,'2012-10-01', '2013-01-13'); # 104
                        //SELECT TIMESTAMPDIFF(WEEK,'2012-10-01', '2013-01-13'); # 14
                        //SELECT TIMESTAMPDIFF(MONTH,'2012-10-01', '2013-01-13'); # 3
                        //SELECT TIMESTAMPDIFF(QUARTER,'2012-10-01', '2013-01-13'); # 1
                        //SELECT TIMESTAMPDIFF(YEAR,'2012-10-01', '2013-01-13'); # 0
                        _end = (string.IsNullOrEmpty(end)) ? "NOW()" : end;
                        return $"timestampdiff({interval}, {start}, {_end})";
                    }
                    else if (dbType == DbType.Sqlite)
                    {
                        int _interval = 1;
                        if (interval == "SECOND") _interval = 24 * 60 * 60;
                        if (interval == "MINUTE") _interval = 24 * 60;
                        if (interval == "HOUR") _interval = 24;
                        if (interval == "DAY") _interval = 1;// DAY及 以下未做测试
                        _end = (string.IsNullOrEmpty(end)) ? "datetime('now','localtime')" : end;
                        return $"Cast((JulianDay({_end}) - JulianDay({start}))*{_interval} As Integer)";
                    }
                    else
                    {
                        throw new Exception("未实现");
                    }
                }
            });
            expMethods.Add(new SqlFuncExternal()
            {
                UniqueMethodName = "ToDateFormat",
                MethodValue = (expInfo, dbType, expContext) =>
                {
                    switch (dbType)
                    {
                        case DbType.SqlServer:
                            return $"CONVERT (VARCHAR (10), {expInfo.Args[0].MemberName}, 121 )";
                        case DbType.MySql:
                            return $"DATE_FORMAT( {expInfo.Args[0].MemberName}, '%Y-%m-%d' ) ";
                        case DbType.Sqlite:
                            return $"date({expInfo.Args[0].MemberName})";
                        case DbType.PostgreSQL:
                        case DbType.Oracle:
                            return $"to_date({expInfo.Args[0].MemberName},yyyy-MM-dd)";
                        default:
                            throw new Exception("未实现");
                    }
                }
            });

            sss.CurrentConnectionConfig.ConfigureExternalServices = new ConfigureExternalServices()
            {
                SqlFuncServices = expMethods //set ext method
            };
        }
        /// <summary>
        /// db.Queryable&lt;Student&gt;().Where(it => MyToString(it.Id) == "1302583").ToList();
        /// 生成的Sql CAST([Id] AS VARCHAR(MAX))
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string MyToString<T>(T str)
        {
            //这里不能写任何实现代码，需要在上面的配置中实现
            throw new NotSupportedException("Can only be used in expressions");
        }
        /// <summary>
        /// 时间戳转日期格式 此处转string为了展示方便(没有毫秒小数点)
        /// </summary>
        /// <param name="str"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static string ConvertTime<T>(T str)
        {
            throw new NotSupportedException("Can only be used in expressions");
        }
        /// <summary>
        /// 日期格式转时间戳
        /// </summary>
        /// <param name="str"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        public static long UNIX_TIMESTAMP<T>(T str)
        {
            throw new NotSupportedException("Can only be used in expressions");
        }

        /// <summary>
        /// 注意必須在GroupBy后使用
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string GroupConcat<T>(T str)
        {
            throw new NotSupportedException("Can only be used in expressions");
        }
        /// <summary>
        /// db.Queryable&lt;Order&gt;().Select(it=> PartitionDateDiff(it.Name, it.CreateTime)).ToList();
        /// SELECT *,ROW_NUMBER() over(partition by `Name` order by abs(datediff(`CreateTime`, NOW() )) ) FROM `Order`
        /// </summary>
        /// <param name="name"></param>
        /// <param name="timeFiled"></param>
        /// <returns></returns>
        public static string PartitionDateDiff(string name, DateTime? timeFiled)
        {
            throw new NotSupportedException("Can only be used in expressions");
        }
        public static string GetDuration(string start, string interval = "SECOND", string end = "")
        {
            throw new NotSupportedException("Can only be used in expressions");
        }
        /// <summary>
        /// db.Queryable&lt;Order&gt;().Select(it=> ToDateFormat(it.CreateTime)).ToList();
        /// </summary>
        /// <param name="dateField"></param>
        /// <returns></returns>
        public static string ToDateFormat(string dateField)
        {
            throw new NotSupportedException("Can only be used in expressions");
        }
    }
}
