-- iot_biz_exec_log execution-context extension
-- Adds trace/replay/audit fields used by BizExecutionContext.

IF COL_LENGTH('dbo.iot_biz_exec_log', 'trace_id') IS NULL
    ALTER TABLE [dbo].[iot_biz_exec_log] ADD [trace_id] NVARCHAR(64) NULL;

IF COL_LENGTH('dbo.iot_biz_exec_log', 'trigger_source') IS NULL
    ALTER TABLE [dbo].[iot_biz_exec_log] ADD [trigger_source] NVARCHAR(64) NULL;

IF COL_LENGTH('dbo.iot_biz_exec_log', 'source_event_id') IS NULL
    ALTER TABLE [dbo].[iot_biz_exec_log] ADD [source_event_id] NVARCHAR(64) NULL;

IF COL_LENGTH('dbo.iot_biz_exec_log', 'template_version') IS NULL
    ALTER TABLE [dbo].[iot_biz_exec_log] ADD [template_version] NVARCHAR(64) NULL;

IF COL_LENGTH('dbo.iot_biz_exec_log', 'snapshot_version') IS NULL
    ALTER TABLE [dbo].[iot_biz_exec_log] ADD [snapshot_version] NVARCHAR(64) NULL;

IF COL_LENGTH('dbo.iot_biz_exec_log', 'operator_id') IS NULL
    ALTER TABLE [dbo].[iot_biz_exec_log] ADD [operator_id] NVARCHAR(64) NULL;

IF COL_LENGTH('dbo.iot_biz_exec_log', 'operator_name') IS NULL
    ALTER TABLE [dbo].[iot_biz_exec_log] ADD [operator_name] NVARCHAR(128) NULL;

IF COL_LENGTH('dbo.iot_biz_exec_log', 'input_snapshot') IS NULL
    ALTER TABLE [dbo].[iot_biz_exec_log] ADD [input_snapshot] NVARCHAR(2000) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_iot_biz_exec_log_trace' AND object_id = OBJECT_ID('dbo.iot_biz_exec_log'))
    CREATE INDEX [IX_iot_biz_exec_log_trace] ON [dbo].[iot_biz_exec_log] ([trace_id]);
