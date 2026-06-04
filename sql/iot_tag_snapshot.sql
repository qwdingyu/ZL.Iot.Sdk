-- ================================================================
-- 标签运行态快照表 (iot_tag_snapshot)
-- 对应模型: iot-sdk/ZL.Dao.IotDevice/Model/iot_tag_snapshot.cs
-- 用途: 从 iot_tag 表剥离运行态字段（value、quality、updated_at），
--       解决"配置表兼具运行态缓存"导致的审计困难和并发冲突问题
-- 创建时间: 2026-04-23
-- ================================================================
IF NOT EXISTS (
    SELECT
        *
    FROM
        sys.objects
    WHERE
        object_id = OBJECT_ID(N'[dbo].[iot_tag_snapshot]')
        AND type in (N'U')
) BEGIN CREATE TABLE [dbo].[iot_tag_snapshot] (
    [tag_id] NVARCHAR(64) NOT NULL PRIMARY KEY,
    -- 标签ID（对应 iot_tag.id）
    [value] NVARCHAR(255) NULL,
    -- 当前值（最新采集值）
    [quality] NVARCHAR(8) NULL,
    -- 质量码（Good=0, Uncertain=1, Bad=2）
    [collect_time] DATETIME NULL,
    -- 采集时间戳（设备端原始采集时间）
    [updated_at] DATETIME NOT NULL DEFAULT GETDATE(),
    -- 快照更新时间（服务端写入时间）
    [device_id] NVARCHAR(64) NULL -- 来源设备ID（便于按设备查询）
);

-- 建议索引
CREATE INDEX [IX_iot_tag_snapshot_device_id] ON [dbo].[iot_tag_snapshot] ([device_id]);

CREATE INDEX [IX_iot_tag_snapshot_updated_at] ON [dbo].[iot_tag_snapshot] ([updated_at]);

END
GO