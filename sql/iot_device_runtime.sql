-- ================================================================
-- 设备运行态表 (iot_device_runtime)
-- 对应模型: iot-sdk/ZL.Dao.IotDevice/Model/iot_device_runtime.cs
-- 用途: 从 iot_device 表剥离运行时状态信息（在线状态、连接状态、ping 状态等），
--       使设备配置表保持静态定义语义，运行态完全外部化
-- 创建时间: 2026-04-23
-- ================================================================
IF NOT EXISTS (
    SELECT
        *
    FROM
        sys.objects
    WHERE
        object_id = OBJECT_ID(N'[dbo].[iot_device_runtime]')
        AND type in (N'U')
) BEGIN CREATE TABLE [dbo].[iot_device_runtime] (
    [device_id] NVARCHAR(64) NOT NULL PRIMARY KEY,
    -- 设备ID（对应 iot_device.id）
    [is_online] BIT NOT NULL DEFAULT 0,
    -- 是否在线
    [is_connected] BIT NOT NULL DEFAULT 0,
    -- 连接是否建立
    [is_ping_ok] BIT NOT NULL DEFAULT 0,
    -- Ping 是否成功
    [last_connect_time] DATETIME NULL,
    -- 最后连接成功时间
    [last_ping_time] DATETIME NULL,
    -- 最后 Ping 时间
    [last_collect_time] DATETIME NULL,
    -- 最后采集成功时间
    [last_error_time] DATETIME NULL,
    -- 最后采集异常时间
    [last_error_msg] NVARCHAR(1000) NULL,
    -- 最后异常消息
    [fail_count] INT NOT NULL DEFAULT 0,
    -- 连续失败次数
    [runtime_status] NVARCHAR(32) NULL,
    -- 运行时状态（Running/Degraded/Offline）
    [updated_at] DATETIME NOT NULL DEFAULT GETDATE(),
    -- 快照更新时间
    [company_id] NVARCHAR(64) NULL -- 公司ID
);

-- 建议索引
CREATE INDEX [IX_iot_device_runtime_company_id] ON [dbo].[iot_device_runtime] ([company_id]);

CREATE INDEX [IX_iot_device_runtime_is_online] ON [dbo].[iot_device_runtime] ([is_online]);

CREATE INDEX [IX_iot_device_runtime_updated_at] ON [dbo].[iot_device_runtime] ([updated_at]);

END
GO