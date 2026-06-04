-- ================================================================
-- 标签关系表 (iot_tag_relation)
-- 对应模型: iot-sdk/ZL.Dao.IotDevice/Model/iot_tag_relation.cs
-- 用途: 替代 iot_tag.tag_sub 字符串字段，将标签间的附属/依赖关系转为表驱动
--       支持高效查询、索引和关系维护，旧字段 tag_sub 保留用于向后兼容
-- 创建时间: 2026-04-23
-- ================================================================
IF NOT EXISTS (
    SELECT
        *
    FROM
        sys.objects
    WHERE
        object_id = OBJECT_ID(N'[dbo].[iot_tag_relation]')
        AND type in (N'U')
) BEGIN CREATE TABLE [dbo].[iot_tag_relation] (
    [id] NVARCHAR(36) NOT NULL DEFAULT NEWID() PRIMARY KEY,
    -- 主键（GUID）
    [master_tag_id] NVARCHAR(64) NOT NULL,
    -- 主标签ID（触发源标签）
    [slave_tag_id] NVARCHAR(64) NOT NULL,
    -- 从属标签ID（被关联的标签）
    [relation_type] NVARCHAR(16) NOT NULL,
    -- 关系类型（subscription/writeback/validate）
    [enable] INT NOT NULL DEFAULT 1,
    -- 是否启用（0未启用，1启用）
    [remark] NVARCHAR(255) NULL,
    -- 备注
    [created_at] DATETIME NULL,
    -- 创建时间
    [created_by] NVARCHAR(64) NULL,
    -- 创建人
    [updated_at] DATETIME NULL,
    -- 更新时间
    [updated_by] NVARCHAR(64) NULL,
    -- 更新人
    CONSTRAINT [PK_iot_tag_relation] PRIMARY KEY CLUSTERED ([id] ASC)
);

-- 建议索引
-- 按主标签快速查询所有关系
CREATE INDEX [IX_iot_tag_relation_master] ON [dbo].[iot_tag_relation] ([master_tag_id]);

-- 按关系类型筛选
CREATE INDEX [IX_iot_tag_relation_type] ON [dbo].[iot_tag_relation] ([relation_type]);

-- 按主标签+关系类型组合查询
CREATE INDEX [IX_iot_tag_relation_master_type] ON [dbo].[iot_tag_relation] ([master_tag_id], [relation_type]);

END
GO