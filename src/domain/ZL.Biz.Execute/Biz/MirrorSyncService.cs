using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZL.Iot.Interface;

namespace ZL.Biz.Execute.Biz
{
    /// <summary>
    /// 高可靠性配置镜像同步服务实现
    /// 针对工业生产环境加固：
    /// 1. 支持 GZip 压缩荷载，应对大规模配置同步
    /// 2. 增强型 Schema 全量探测与自愈
    /// 3. 与 BizCfgExecutor 缓存联动，确保热更新实时生效
    /// 4. 严格表名安全校验
    /// </summary>
    public class MirrorSyncService : IMirrorSyncService
    {
        private readonly ILogger<MirrorSyncService> _logger;
        private readonly ISqlExecutor _sqlExecutor;

        public MirrorSyncService(ILogger<MirrorSyncService> logger, ISqlExecutor sqlExecutor)
        {
            _logger = logger;
            _sqlExecutor = sqlExecutor;
        }

        public async Task<bool> ApplySnapshotAsync(string gatewayId, string version, string jsonPayload)
        {
            _logger.LogInformation("MirrorSync: Applying dynamic snapshot version {Version} for Gateway {GatewayId}", version, gatewayId);
            
            try
            {
                if (string.IsNullOrWhiteSpace(jsonPayload)) return false;

                // 工业级加固：自动检测并处理 GZip 压缩
                string decompressedJson = jsonPayload;
                if (IsGZip(jsonPayload))
                {
                    decompressedJson = Decompress(jsonPayload);
                    _logger.LogDebug("MirrorSync: Payload decompressed via GZip.");
                }

                var snapshot = JsonConvert.DeserializeObject<GenericSnapshotDto>(decompressedJson);
                if (snapshot == null || snapshot.Tables == null) 
                {
                    _logger.LogWarning("MirrorSync: Invalid payload format.");
                    return false;
                }

                var localVersion = await GetLocalVersionAsync(gatewayId);
                if (!string.IsNullOrEmpty(localVersion) && string.Compare(version, localVersion) < 0)
                {
                    _logger.LogWarning("MirrorSync: Version conflict. Cloud version {VCloud} is older than local {VLocal}.", version, localVersion);
                    return true; 
                }

                _sqlExecutor.BeginTransaction();
                try
                {
                    foreach (var packet in snapshot.Tables)
                    {
                        await SyncTablePacketAsync(packet);
                    }

                    await UpdateMetadataAsync(gatewayId, version);

                    _sqlExecutor.CommitTransaction();

                    // 关键：同步成功后强制清除业务执行器缓存，确保下一波 PLC 触发加载最新配置
                    BizCfgExecutor.ClearCache();

                    _logger.LogInformation("MirrorSync: Full snapshot applied successfully. Version: {Version}", version);
                    return true;
                }
                catch (Exception ex)
                {
                    _sqlExecutor.RollbackTransaction();
                    _logger.LogError(ex, "MirrorSync: Transaction failed, all changes rolled back.");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MirrorSync: Critical error applying snapshot.");
                return false;
            }
        }

        private async Task SyncTablePacketAsync(SyncTablePacket packet)
        {
            if (string.IsNullOrEmpty(packet.TableName)) return;

            // 表名白名单校验
            if (!System.Text.RegularExpressions.Regex.IsMatch(packet.TableName, @"^[a-zA-Z0-9_]+$"))
            {
                throw new SecurityException($"Forbidden table name: {packet.TableName}");
            }

            Dictionary<string, object> combinedSchema = null;
            if (packet.Rows != null && packet.Rows.Count > 0)
            {
                // 工业级加固：全量扫描探测 Schema，防止由于首行 Null 导致的类型推断错误
                combinedSchema = ProbeFullSchema(packet.Rows); 
                await EnsureTableSchemaAsync(packet.TableName, combinedSchema);
            }

            if (packet.Strategy == SyncStrategy.Overwrite)
            {
                await _sqlExecutor.ExecuteNonQueryAsync($"DELETE FROM {packet.TableName}");
            }

            if (packet.Rows == null || packet.Rows.Count == 0) return;

            var columns = combinedSchema?.Keys.ToList() ?? packet.Rows.First().Keys.ToList();
            var placeholders = columns.Select(c => $"@{c}").ToList();
            
            // 工业级加固：Upsert 模式使用 INSERT OR REPLACE
            string verb = (packet.Strategy == SyncStrategy.Upsert) ? "INSERT OR REPLACE" : "INSERT";
            string insertSql = $"{verb} INTO {packet.TableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})";

            var batchParams = packet.Rows.Select(row => {
                var p = new Dictionary<string, object>();
                foreach (var col in columns)
                {
                    p[$"@{col}"] = (row.TryGetValue(col, out var val) && val != null) ? val : DBNull.Value;
                }
                return p;
            });

            await _sqlExecutor.ExecuteBatchNonQueryAsync(insertSql, batchParams);
            _logger.LogDebug("MirrorSync: Table {TableName} synced via {Verb}, Rows: {Count}", packet.TableName, verb, packet.Rows.Count);
        }

        private Dictionary<string, object> ProbeFullSchema(IEnumerable<Dictionary<string, object>> rows)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                foreach (var kv in row)
                {
                    if (!result.ContainsKey(kv.Key) || (result[kv.Key] == null && kv.Value != null))
                    {
                        result[kv.Key] = kv.Value;
                    }
                }
            }
            return result;
        }

        private async Task EnsureTableSchemaAsync(string tableName, Dictionary<string, object> schemaModel)
        {
            var sb = new StringBuilder();
            sb.Append($"CREATE TABLE IF NOT EXISTS {tableName} (");
            
            // 约定 ID 为主键
            var colDefs = schemaModel.Select(kv => {
                string type = GetSqliteType(kv.Value);
                if (kv.Key.Equals("id", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{kv.Key} {type} PRIMARY KEY";
                }
                return $"{kv.Key} {type}";
            });
            
            sb.Append(string.Join(", ", colDefs));
            sb.Append(")");
            await _sqlExecutor.ExecuteNonQueryAsync(sb.ToString());

            // 增量字段补齐 (Schema Evolution)
            var existingCols = await _sqlExecutor.ExecuteQueryAsync($"PRAGMA table_info({tableName})");
            var colNames = existingCols.Select(c => c["name"]?.ToString().ToLower()).ToHashSet();

            foreach (var kv in schemaModel)
            {
                if (!colNames.Contains(kv.Key.ToLower()))
                {
                    _logger.LogInformation("MirrorSync: Auto-evolving table {Table}, adding column {Col}", tableName, kv.Key);
                    await _sqlExecutor.ExecuteNonQueryAsync($"ALTER TABLE {tableName} ADD COLUMN {kv.Key} {GetSqliteType(kv.Value)}");
                }
            }
        }

        private string GetSqliteType(object value)
        {
            if (value == null) return "TEXT";
            if (value is int || value is long || value is short || value is byte) return "INTEGER";
            if (value is double || value is float || value is decimal) return "REAL";
            if (value is bool) return "INTEGER";
            return "TEXT";
        }

        public async Task<string> GetLocalVersionAsync(string gatewayId)
        {
            try {
                string sql = "SELECT version FROM edge_sync_metadata WHERE gateway_id = @gid LIMIT 1";
                var res = await _sqlExecutor.ExecuteScalarAsync(sql, new Dictionary<string, object> { { "@gid", gatewayId } });
                return res?.ToString();
            } catch { return null; }
        }

        private async Task UpdateMetadataAsync(string gatewayId, string version)
        {
            await _sqlExecutor.ExecuteNonQueryAsync(@"
                CREATE TABLE IF NOT EXISTS edge_sync_metadata (
                    gateway_id TEXT PRIMARY KEY,
                    version TEXT,
                    last_sync_time TEXT
                )");

            await _sqlExecutor.ExecuteNonQueryAsync(@"
                INSERT INTO edge_sync_metadata (gateway_id, version, last_sync_time)
                VALUES (@gid, @v, @t)
                ON CONFLICT(gateway_id) DO UPDATE SET version=@v, last_sync_time=@t",
                new Dictionary<string, object> {
                    { "@gid", gatewayId }, { "@v", version }, { "@t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
                });
        }

        private bool IsGZip(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Length < 4) return false;
            try {
                byte[] bytes = Convert.FromBase64String(input);
                return bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b;
            } catch { return false; }
        }

        private string Decompress(string compressedBase64)
        {
            byte[] buffer = Convert.FromBase64String(compressedBase64);
            using var ms = new MemoryStream(buffer);
            using var gzip = new GZipStream(ms, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private class GenericSnapshotDto { public List<SyncTablePacket> Tables { get; set; } }
        private class SyncTablePacket { 
            public string TableName { get; set; } 
            public SyncStrategy Strategy { get; set; } = SyncStrategy.Overwrite;
            public List<Dictionary<string, object>> Rows { get; set; } 
        }
        public enum SyncStrategy { Overwrite = 0, Upsert = 1 }
        private class SecurityException : Exception { public SecurityException(string msg) : base(msg) { } }
    }
}
