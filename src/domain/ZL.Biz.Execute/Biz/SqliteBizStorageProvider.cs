using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZL.Dao.IotDevice;
using ZL.Iot.Interface;

namespace ZL.Biz.Execute.Biz
{
    /// <summary>
    /// SQLite 存储提供者实现（边缘侧专用）
    /// 深度优化：支持与云端 SqlSugarBizStorageProvider 对等的数据映射逻辑
    /// </summary>
    public class SqliteBizStorageProvider : IBizStorageProvider
    {
        private readonly ISqlExecutor _sqlExecutor;

        public SqliteBizStorageProvider(ISqlExecutor sqlExecutor)
        {
            _sqlExecutor = sqlExecutor;
        }

        public string EngineType => "Sqlite";
        public ISqlExecutor SqlExecutor => _sqlExecutor;

        public async Task<(List<TExe> exes, List<TVal> vals)> GetBizConfigAsync<TExe, TVal>(string tagId)
            where TExe : class, new()
            where TVal : class, new()
        {
            // 使用镜像表，确保边缘侧在离线状态下也能读取业务配置
            var exeData = await _sqlExecutor.ExecuteQueryAsync(
                "SELECT * FROM iot_exe_mirror WHERE tag_id = @tagId AND enable = 1 ORDER BY exe_order",
                new Dictionary<string, object> { { "@tagId", tagId } });

            var valData = await _sqlExecutor.ExecuteQueryAsync(
                "SELECT * FROM iot_exeval_mirror WHERE tag_id = @tagId ORDER BY exe_order",
                new Dictionary<string, object> { { "@tagId", tagId } });

            var exes = exeData.Select(d => MapToType<TExe>(d)).ToList();
            var vals = valData.Select(d => MapToType<TVal>(d)).ToList();

            return (exes, vals);
        }

        public async Task RecordOfflineCommandAsync(string bizCode, string cmdType, string payload, string traceId)
        {
            string id = Guid.NewGuid().ToString("N");
            // 写入本地离线指令缓冲区，等待 ReplayScheduler 上传
            string insertSql = @"
                INSERT INTO edge_offline_commands (id, trace_id, biz_code, cmd_type, payload, create_time, sync_status)
                VALUES (@id, @traceId, @bizCode, @cmdType, @payload, @createTime, 0)";

            var parameters = new Dictionary<string, object>
            {
                { "@id", id },
                { "@traceId", traceId ?? "" },
                { "@bizCode", bizCode },
                { "@cmdType", cmdType },
                { "@payload", payload },
                { "@createTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
            };

            await _sqlExecutor.ExecuteNonQueryAsync(insertSql, parameters);
        }

        private T MapToType<T>(Dictionary<string, object> d) where T : class, new()
        {
            var obj = new T();
            var props = typeof(T).GetProperties();
            foreach (var prop in props)
            {
                // 忽略大小写匹配属性名
                var key = d.Keys.FirstOrDefault(k => k.Equals(prop.Name, StringComparison.OrdinalIgnoreCase));
                if (key != null)
                {
                    var val = d[key];
                    if (val != DBNull.Value && val != null)
                    {
                        try
                        {
                            // 处理数值类型的类型转换
                            object convertedValue;
                            if (prop.PropertyType == typeof(int) || prop.PropertyType == typeof(int?))
                                convertedValue = Convert.ToInt32(val);
                            else if (prop.PropertyType == typeof(long) || prop.PropertyType == typeof(long?))
                                convertedValue = Convert.ToInt64(val);
                            else if (prop.PropertyType == typeof(float) || prop.PropertyType == typeof(float?))
                                convertedValue = Convert.ToSingle(val);
                            else if (prop.PropertyType == typeof(double) || prop.PropertyType == typeof(double?))
                                convertedValue = Convert.ToDouble(val);
                            else if (prop.PropertyType == typeof(bool) || prop.PropertyType == typeof(bool?))
                                convertedValue = Convert.ToBoolean(val);
                            else
                                convertedValue = Convert.ChangeType(val, prop.PropertyType);

                            prop.SetValue(obj, convertedValue);
                        }
                        catch { /* Skip mismatch */ }
                    }
                }
            }
            return obj;
        }
    }
}
