-- ================================================================
-- 业务配置执行审计日志表 (iot_biz_exec_log)
-- 对应模型: iot-sdk/ZL.Dao.IotDevice/Model/iot_biz_exec_log.cs
-- 用途: 记录 BizCfgExe 每次执行的轨迹（成功/失败/跳过），
--       包括 tagId、bizCode、exe_order、脚本快照、错误信息、耗时
-- 创建时间: 2026-04-23
-- ================================================================
IF NOT EXISTS (
    SELECT
        *
    FROM
        sys.objects
    WHERE
        object_id = OBJECT_ID(N'[dbo].[iot_biz_exec_log]')
        AND type in (N'U')
) BEGIN CREATE TABLE [dbo].[iot_biz_exec_log] (
    [id] BIGINT NOT NULL IDENTITY(1, 1) PRIMARY KEY,
    [tag_id] NVARCHAR(64) NOT NULL,
    -- 触发执行的标签ID
    [biz_code] NVARCHAR(32) NULL,
    -- 业务模式代码（SET/GET/QTY/PLAN 等）
    [exe_order] NVARCHAR(8) NULL,
    -- 执行顺序号
    [exe_type] NVARCHAR(8) NULL,
    -- 执行类型（S=查询/U=更新/B=批量）
    [script_snapshot] NVARCHAR(2000) NULL,
    -- 脚本快照（脱敏后，最大 2000 字符）
    [err_msg] NVARCHAR(2000) NULL,
    -- 错误信息
    [result] NVARCHAR(16) NOT NULL,
    -- 执行结果：OK / FAIL / SKIP
    [duration_ms] BIGINT NULL,
    -- 执行耗时（毫秒）
    [create_time] DATETIME NOT NULL DEFAULT GETDATE(),
    -- 执行时间
    [device_id] NVARCHAR(64) NULL,
    -- 设备ID（可选）
    [company_id] NVARCHAR(64) NULL -- 公司ID（可选）
);

-- 建议索引
CREATE INDEX [IX_iot_biz_exec_log_tag_id] ON [dbo].[iot_biz_exec_log] ([tag_id]);

CREATE INDEX [IX_iot_biz_exec_log_device_id] ON [dbo].[iot_biz_exec_log] ([device_id]);

CREATE INDEX [IX_iot_biz_exec_log_result] ON [dbo].[iot_biz_exec_log] ([result]);

CREATE INDEX [IX_iot_biz_exec_log_create_time] ON [dbo].[iot_biz_exec_log] ([create_time]);

END
GO