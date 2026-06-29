using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using ZL.IotHub;
using ZL.IotHub.Core;
using ZL.IotHub.Monitoring;
using ZL.Tag;
using ZL.Iot.Controls.Common;

namespace ZL.Iot.Controls.Controls
{
    /// <summary>
    /// 点表监控表格控件 —— 核心采集与展示组件。
    ///
    /// 职责：
    /// 1. 显示用户配置的标签列表（地址/类型/值 等列）
    /// 2. 后台独立线程循环执行批量读取，定时更新 UI
    /// 3. 支持选中行写入、批量写入（同值/多值校验）
    /// 4. 点表配置保存/加载（XML 文件）
    /// 5. 值变化时触发日志输出和趋势图事件
    ///
    /// 线程模型：
    /// - UI 线程：构造/事件/DataGridView 操作
    /// - 后台线程：RefreshDataAsync 在 Task.Run 中运行
    /// - 跨线程通信：BeginInvoke 投递 UI 更新（避免采集线程被 UI 速度拖慢）
    /// - 线程退出：CancellationTokenSource 控制在控件被 Disposed 时停止采集
    ///
    /// 性能关键点（已处理）：
    /// - 批量采集使用 BatchMonitor 的块合并算法，减少 PLC 通讯次数
    /// - UI 更新只修改值变化的单元格，不刷新整行
    /// - BeginInvoke 而非 Invoke，避免采集线程阻塞等待 UI
    /// - RowPostPaint 使用 TextRenderer.DrawText 替代 DrawString + MeasureText
    /// </summary>
    public partial class UcTagTable : UserControl
    {
        // DataGridView 列索引常量（与 Designer.cs 中列顺序保持一致）
        int TagAddressCellIndex = 2;  // "设备地址"列索引
        int TagValCellIndex = 6;      // "值"列索引——高频更新列

        /// <summary>遗留锁对象（目前未实际使用，保留兼容）</summary>
        readonly object syncObj = new object();

        /// <summary>
        /// 标签行缓存字典。Key=DataGridViewRow 引用，Value=标签配置。
        /// 该字典在 UpdateTagItemListCore 中重建，后台采集线程通过 BuildTagKey 查找匹配行。
        /// 注意：行引用在 DataGridView 删除/重新排序后会失效，所以每次更新操作后调用
        /// RequestUpdateTagItemList 重建缓存。
        /// </summary>
        ConcurrentDictionary<DataGridViewRow, TagItem> tagDgvRowDic = new ConcurrentDictionary<DataGridViewRow, TagItem>();

        CancellationTokenSource cancellationTokenSource;

        ParallelOptions parallelOption = new ParallelOptions()
        {
            MaxDegreeOfParallelism = System.Environment.ProcessorCount
        };

        /// <summary>日志输出事件（由外部 UcConsolePanel 消费）</summary>
        public event Action<String> OnLogs;

        /// <summary>
        /// 采集循环中每次值变化时触发，供趋势图（UcTrendPanel）消费。
        /// 参数顺序：(address, value, dataType)
        /// 注意：此事件的消费者在 Frm_Main.OpenProtocolTab 中订阅，Tab 关闭时取消订阅
        /// 以避免事件泄漏导致对象无法 GC。
        /// </summary>
        public event Action<string, string, string> OnValueCollected;

        /// <summary>采集模式（当前固定为"ALL"，保留字段供后续扩展）</summary>
        public string CollectionMode = "ALL";

        /// <summary>导出文件默认名称</summary>
        public string FileName { get; set; }

        /// <summary>
        /// S7 字符串前导长度标记模式。
        /// 西门子 S7 协议中，字符串类型在数据前有两个字节表示"最大长度"和"实际长度"，
        /// 称为前导（leader）。启用此模式后，写入字符串会自动计算并写入前导长度。
        /// </summary>
        private bool stringHasLeader;
        public bool StringHasLeader
        {
            get => stringHasLeader;
            set
            {
                stringHasLeader = value;
                if (_batchMonitor != null) _batchMonitor.StringHasLeader = value;
            }
        }

        /// <summary>
        /// 批量写入结果汇总。
        /// 记录成功/失败计数和错误详情，用于批量操作完成后展示摘要对话框。
        /// </summary>
        private sealed class WriteSummary
        {
            public int SuccessCount { get; set; }
            public int FailedCount { get; set; }
            public List<string> Errors { get; } = new List<string>();
        }

        /// <summary>采集计划构建同步锁（BatchMonitor.BuildPlan 调用时锁定）</summary>
        private readonly object readPlanSyncObj = new object();

        /// <summary>
        /// 标签列表更新防抖定时器。
        /// 用户在点表编辑单元格（如改地址/类型）时，此定时器延迟 80ms 后统一重建标签缓存，
        /// 避免每次单元格修改都触发全量重建。
        /// </summary>
        private readonly System.Windows.Forms.Timer tagListUpdateDebounceTimer = new System.Windows.Forms.Timer();
        private bool tagListUpdatePending;
        private const int TagListUpdateDebounceMs = 80;

        /// <summary>
        /// 事件绑定守卫标志。
        /// 防止 DataTableControl_Load 被多次触发时重复绑定事件。
        /// 这是一个典型的 WinForms 防御模式——Load 事件可能在设计器和运行时多次触发。
        /// </summary>
        private bool eventsBound;

        /// <summary>右键菜单目标行（鼠标按下时记录，供菜单打开时使用）</summary>
        private DataGridViewRow contextMenuTargetRow;
        private ToolStripMenuItem tsmiWriteBatchSameValue;
        private bool quickWriteUiBuilt;

        // 快速写入栏的控件引用（运行时创建，非 Designer 预置）
        private Label lblQuickWriteValue;
        private ComboBox cmbQuickWriteValue;
        private Button btnQuickUseCurrentValue;
        private Button btnQuickWriteSelected;
        private Button btnQuickWriteVerify;

        /// <summary>快速写入历史记录（最近 16 条，存入 ComboBox 下拉）</summary>
        private readonly List<string> quickWriteHistory = new List<string>();
        private const int QuickWriteHistoryLimit = 16;
        private Button _btnExportCsv;
        private Button _btnExportCsvConfig;
        private Button _btnImportCsvConfig;
        private Button _btnBatchGenerate;
        private Button _btnLoadDemo;
        private CheckBox chkNoiseFilter;
        private bool topPanelLayoutEventsBound;

        /// <summary>当前协议类型（ProtocolCatalog.CanonicalId，与 NativeProtocolRegistry/SimulationRegistry 的 Key 一致）</summary>
        public string ProtocolType { get; set; } = "siemens-s7";

        /// <summary>
        /// 各协议类型的示例地址库。
        /// 每种数据类型配一个典型地址，供"加载示例"按钮使用。
        /// 键 = PlcSimulator.Core.ProtocolCatalog.CanonicalId（与 HardcodedCatalogProvider.ProtocolType 一致）。
        /// </summary>
        private static readonly Dictionary<string, (string Name, string Address, string Type)[]> DemoAddresses = new()
        {
            ["siemens-s7"] = new[] {
                ("Bool_0",   "DB1.DBX0.0", "bool"),
                ("Byte_10",  "DB1.DBB10",  "byte"),
                ("Int_12",   "DB1.DBW12",  "short"),
                ("UInt_14",  "DB1.DBW14",  "ushort"),
                ("DInt_20",  "DB1.DBD20",  "int"),
                ("Real_0",   "DB1.DBD0",   "float"),
                ("String_30","DB1.DBB30",  "string"),
                ("Double_40","DB1.DBD40",  "double"),
            },
            ["modbus-tcp"] = new[] {
                ("Coil_0",   "00001", "bool"),
                ("DI_0",     "10001", "bool"),
                ("HR_0",     "40001", "short"),
                ("HR_1",     "40002", "ushort"),
                ("HR_2",     "40003", "int"),
                ("HR_4",     "40005", "float"),
                ("IR_0",     "30001", "short"),
            },
            ["melsec-mc"] = new[] {
                ("D100",    "D100",  "short"),
                ("D102",    "D102",  "ushort"),
                ("D104",    "D104",  "int"),
                ("D108",    "D108",  "float"),
                ("M0",      "M0",    "bool"),
                ("X0",      "X0",    "bool"),
                ("Y0",      "Y0",    "bool"),
            },
            ["omron-fins-tcp"] = new[] {
                ("D100",    "D100",  "short"),
                ("D102",    "D102",  "ushort"),
                ("D104",    "D104",  "int"),
                ("D108",    "D108",  "float"),
                ("W0_00",   "W0.00", "bool"),
                ("CIO100",  "CIO100","short"),
                ("H100",    "H100",  "int"),
            },
            ["allen-bradley"] = new[] {
                ("B3_0",    "B3:0",   "short"),
                ("B3_1",    "B3:1",   "int"),
                ("F8_0",    "F8:0",   "float"),
                ("N7_0",    "N7:0",   "short"),
                ("N7_2",    "N7:2",   "int"),
                ("B3_0_0",  "B3:0/0", "bool"),
                ("F8_1",    "F8:1",   "double"),
            },
            ["beckhoff-ads"] = new[] {
                ("MAIN_I",  "MAIN.I",  "short"),
                ("MAIN_DI", "MAIN.DI", "int"),
                ("MAIN_R",  "MAIN.R",  "float"),
                ("MAIN_B",  "MAIN.B",  "bool"),
                ("MAIN_S",  "MAIN.S",  "string"),
            },
            ["fatek"] = new[] {
                ("R0",     "R0",   "short"),
                ("R2",     "R2",   "int"),
                ("R4",     "R4",   "float"),
                ("X0",     "X0",   "bool"),
                ("Y0",     "Y0",   "bool"),
                ("M0",     "M0",   "bool"),
            },
            ["keyence-mc"] = new[] {
                ("D100",   "D100", "short"),
                ("D102",   "D102", "int"),
                ("D104",   "D104", "float"),
                ("R0",     "R0",   "bool"),
                ("MR0",    "MR0",  "bool"),
            },
            ["fanuc"] = new[] {
                ("R1",     "R[1]",   "short"),
                ("R2",     "R[2]",   "int"),
                ("R3",     "R[3]",   "float"),
                ("DI1",    "DI[1]",  "bool"),
                ("DO1",    "DO[1]",  "bool"),
            },
        };

        /// <summary>是否在 BatchMonitor 的 Plan 日志中输出详细合并信息</summary>
        public bool EnableReadPlanLogs { get; set; } = true;

        /// <summary>采集线程是否正在运行</summary>
        public bool IsRefreshing => cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested;

        /// <summary>
        /// 值变化缓存字典。
        /// Key = 标签地址，Value = 上次记录的值。
        /// 用于 SetLogs 判断值是否变化，避免重复输出"值未变"的日志。
        /// 使用 OrdinalIgnoreCase 比较器，因为 PLC 地址大小写不敏感（如 DB1.DBD0 == db1.dbd0）。
        /// </summary>
        private readonly ConcurrentDictionary<string, string> valueChangeCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public UcTagTable()
        {
            InitializeComponent();

            // 设置"值"列的单元格样式：左对齐（默认居中）
            DataGridViewCellStyle style = new DataGridViewCellStyle();
            style.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dgv_Tags.Columns[TagValCellIndex].DefaultCellStyle = style;

            // 订阅行号绘制事件（RowPostPaint 在 DataGridView 每次重绘时触发行号显示）
            dgv_Tags.RowPostPaint += dgv_Tags_RowPostPaint;

            // 启用 DataGridView 双缓冲减少绘制闪烁
            // DoubleBuffered 是 protected 属性，通过反射设置
            Type dgvType = dgv_Tags.GetType();
            System.Reflection.PropertyInfo pi = dgvType.GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (pi != null) pi.SetValue(dgv_Tags, true, null);

            // 在 Designer 生成的右键菜单中追加"批量同值写入"项
            tsmiWriteBatchSameValue = new ToolStripMenuItem("批量同值写入") { Name = "TSMI_WriteBatchSameValue" };
            tsmiWriteBatchSameValue.Click += ToolStripMenuItem_Click;
            contextMenuStrip1.Items.Add(new ToolStripSeparator());
            contextMenuStrip1.Items.Add(tsmiWriteBatchSameValue);

            tagListUpdateDebounceTimer.Interval = TagListUpdateDebounceMs;
            tagListUpdateDebounceTimer.Tick += TagListUpdateDebounceTimer_Tick;

            // 控件 Dispose 时统一清理所有运行时动态创建的资源
            // 包括：防抖定时器、右键菜单事件、快速写入栏事件、分栏布局事件
            // 注意：Designer 预置控件由其 components 容器自动管理，不需要在此处手动清理
            Disposed += (_, __) =>
            {
                dgv_Tags.RowPostPaint -= dgv_Tags_RowPostPaint;
                tagListUpdateDebounceTimer.Stop();
                tagListUpdateDebounceTimer.Dispose();
                contextMenuStrip1.Opening -= contextMenuStrip1_Opening;
                dgv_Tags.CellMouseDown -= dgv_Tags_CellMouseDown;
                if (splitContainer1?.Panel1 != null)
                    splitContainer1.Panel1.SizeChanged -= Panel1_SizeChanged;
                if (cmbQuickWriteValue != null) cmbQuickWriteValue.KeyDown -= cmbQuickWriteValue_KeyDown;
                if (btnQuickUseCurrentValue != null) btnQuickUseCurrentValue.Click -= btnQuickUseCurrentValue_Click;
                if (btnQuickWriteSelected != null) btnQuickWriteSelected.Click -= btnQuickWriteSelected_Click;
                if (btnQuickWriteVerify != null) btnQuickWriteVerify.Click -= btnQuickWriteVerify_Click;
            };
        }

        /// <summary>
        /// 绑定 PLC 设备实例到点表控件。
        /// 每次连接成功（或切换设备）时由 Frm_Main 调用。
        /// 内部创建 BatchMonitor——这是批量采集的核心引擎，负责将多个标签按地址合并为
        /// 最少通讯次数的读取请求。
        ///
        /// 调用时机：
        /// - 连接真实 PLC 成功后（Frm_Main.ConnectionChanged handler）
        /// - 断开连接时调用 ConnectClose 清除引用
        ///
        /// 注意：device 为 null 时清除 BatchMonitor 引用，但不清理点表数据，
        /// 用户看到的是最后一次采集的快照值。
        /// </summary>
        public void SetPlcDevice(IPlcDevice device)
        {
            this.device = device;
            if (device != null)
            {
                _batchMonitor?.Dispose();
                _batchMonitor = new BatchMonitor(device)
                {
                    // BatchMonitor 的内部日志通过委托透传到 UI 层显示
                    OnLog = msg => OnLogs?.Invoke(msg),
                    EnablePlanLogs = EnableReadPlanLogs,
                    StringHasLeader = StringHasLeader,
                };
                // 批量读取合并参数：
                // maxGap=200：地址间隔小于 200 字节的相邻地址合并为同一个读取请求
                // maxBlockSize=220：单次读取最大不超过 220 字节（PLC 报文长度限制）
                // minEfficiencyPercent=40：合并后效率低于 40% 则不合并
                _batchMonitor.ConfigureBatchRead(200, 220, 40);
            }
            else
            {
                _batchMonitor?.Dispose();
                _batchMonitor = null;
            }
        }

        private IPlcDevice device;
        private BatchMonitor _batchMonitor;

        /// <summary>动态调整批量读取合并参数（暴露给外部配置 UI）</summary>
        public void ConfigureBatchRead(int maxGap = 200, int maxBlockSize = 220, int minEfficiencyPercent = 40)
        {
            _batchMonitor?.ConfigureBatchRead(maxGap, maxBlockSize, minEfficiencyPercent);
        }

        /// <summary>设置 S7 字符串前导长度模式（暴露给外部配置 UI）</summary>
        public void SetStringLeaderMode(bool hasLeader)
        {
            StringHasLeader = hasLeader;
        }

        /// <summary>获取当前点表中配置了地址的有效标签数量</summary>
        public int GetConfiguredTagCount()
        {
            int count = 0;
            foreach (DataGridViewRow row in dgv_Tags.Rows)
            {
                if (row == null || row.IsNewRow) continue;
                if (!string.IsNullOrWhiteSpace(row.Cells[TagAddressCellIndex].Value?.ToString()))
                    count++;
            }
            return count;
        }

        public bool TryLoadDataTableFromFile(string filePath, bool clearExisting, out int loadedCount, out string message)
        {
            loadedCount = 0;
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(filePath)) { message = "点表路径为空"; return false; }
            if (!File.Exists(filePath)) { message = $"点表不存在: {filePath}"; return false; }
            try
            {
                if (clearExisting) { if (IsRefreshing) cancellationTokenSource?.Cancel(); dgv_Tags.Rows.Clear(); }
                XElement element = XElement.Parse(File.ReadAllText(filePath, Encoding.UTF8));
                loadedCount = LoadDataTable(element);
                message = $"加载成功，Tag数量: {loadedCount}";
                return true;
            }
            catch (Exception ex) { message = $"点表加载失败: {ex.Message}"; return false; }
        }

        /// <summary>
        /// 启动后台批量采集循环。
        /// 
        /// 调用逻辑：
        /// - 无设备已绑定 → 提示"未连接"
        /// - 点表为空 → 提示"请添加点表"
        /// - 采集已运行 → 返回 true（不重复启动）
        /// 
        /// 启动流程：
        /// 1. 新的 CancellationTokenSource
        /// 2. 按钮变为"停止刷新"（绿色高亮）
        /// 3. 读取轮询间隔（毫秒）
        /// 4. Task.Run 启动后台循环
        /// 
        /// 停止流程：
        /// - 用户点击"停止刷新" → btn_Refresh_Click → cancellationTokenSource.Cancel()
        /// - 控件被 Dispose → Disposed 事件处理（见构造函数）
        /// - RefreshDataAsync 内部检测到取消 → 循环结束
        /// 
        /// 后台线程退出后，通过 Invoke 回到 UI 线程恢复按钮状态。
        /// 这里用 Invoke 而非 BeginInvoke 是因为这是"最终的 UI 状态恢复"，
        /// 不需要频繁执行，且需要确保在控件被 Dispose 前完成。
        /// </summary>
        public bool TryStartRefresh(bool silent, out string reason)
        {
            if (device == null) { reason = "设备为断开或未连接，无法读取! "; if (!silent) MessageBox.Show(reason); return false; }
            if (IsDataGridViewEmpty(dgv_Tags)) { reason = "点表为空，请添加点表！"; if (!silent) MessageBox.Show(reason); return false; }
            if (IsRefreshing) { reason = "刷新已在运行"; return true; }

            cancellationTokenSource?.Dispose();
            cancellationTokenSource = new CancellationTokenSource();

            btn_Refresh.Text = "停止刷新";
            btn_Refresh.BackColor = Color.Lime;

            if (int.TryParse(textBox_time.Text, out int ts)) timeSleep = ts;
            RequestUpdateTagItemList(true);

            // 启动后台采集线程（Task.Run），RefreshDataAsync 在其中循环直到取消
            Task.Run(async () =>
            {
                try { await RefreshDataAsync(cancellationTokenSource.Token); }
                catch (Exception ex) { OnLogs?.Invoke($"刷新异常被取消: {ex.Message}"); }
                finally
                {
                    // 采集结束后恢复按钮状态
                    // 使用 BeginInvoke 异步投递，避免 Task.Run 的 finally 块中
                    // 使用同步 Invoke 时 UI 线程卡在等待采集完成导致死锁
                    if (!this.IsDisposed)
                        this.BeginInvoke(new Action(() =>
                        {
                            if (this.IsDisposed) return;
                            btn_Refresh.Text = "开始刷新";
                            btn_Refresh.BackColor = Theme.AppTheme.BgToolbar;
                        }));
                }
            });
            reason = string.Empty;
            return true;
        }

        public bool StartRefresh() => TryStartRefresh(true, out _);

        #region 初始化属性事件
        private void DataTableControl_Load(object sender, EventArgs e)
        {
            setRefreshEnable(false);
            EnsureQuickWriteUi();
            if (!topPanelLayoutEventsBound && splitContainer1?.Panel1 != null)
            {
                splitContainer1.Panel1.SizeChanged += Panel1_SizeChanged;
                topPanelLayoutEventsBound = true;
            }
            if (!eventsBound)
            {
                btn_Refresh.Click += btn_Refresh_Click;
                dgv_Tags.SizeChanged += dgv_Tags_SizeChanged;
                ck_AllTags.CheckedChanged += ck_AllTags_CheckedChanged;
                dgv_Tags.Scroll += dgv_Tags_Scroll;
                dgv_Tags.CellMouseDown += dgv_Tags_CellMouseDown;
                contextMenuStrip1.Opening += contextMenuStrip1_Opening;
                eventsBound = true;
            }
            CollectionMode = "ALL";
            ck_AllTags.Checked = true;
            ck_AllTags.Enabled = false;
            dgv_Tags_SizeChanged(null, e);
            btn_Refresh.Text = "开始刷新";
            ApplyTopPanelLayout();
        }

        private void ck_AllTags_CheckedChanged(object sender, EventArgs e)
        {
            if (!ck_AllTags.Checked) { ck_AllTags.Checked = true; return; }
            CollectionMode = "ALL";
            RequestUpdateTagItemList(true);
        }

        private void dgv_Tags_Scroll(object sender, ScrollEventArgs e) { }

        private void dgv_Tags_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            if (e.RowIndex < 0 || e.RowIndex >= dgv_Tags.Rows.Count) return;
            var row = dgv_Tags.Rows[e.RowIndex];
            if (row.IsNewRow) return;
            if (!row.Selected) { dgv_Tags.ClearSelection(); row.Selected = true; }
            int ci = e.ColumnIndex >= 0 ? e.ColumnIndex : TagValCellIndex;
            if (ci >= 0 && ci < row.Cells.Count) dgv_Tags.CurrentCell = row.Cells[ci];
            contextMenuTargetRow = row;
        }

        private void contextMenuStrip1_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var rows = GetActionRows();
            bool has = rows.Count > 0;
            TSMI_del.Enabled = has; TSMI_Save.Enabled = has;
            TSMI_Write.Enabled = has && device != null;
            if (tsmiWriteBatchSameValue != null) tsmiWriteBatchSameValue.Enabled = has && device != null;
            if (!has) e.Cancel = true;
        }

        private List<DataGridViewRow> GetActionRows()
        {
            var rows = new List<DataGridViewRow>();
            foreach (DataGridViewRow r in dgv_Tags.SelectedRows) { if (r != null && !r.IsNewRow) rows.Add(r); }
            if (rows.Count == 0 && contextMenuTargetRow != null && !contextMenuTargetRow.IsNewRow && contextMenuTargetRow.DataGridView == dgv_Tags)
                rows.Add(contextMenuTargetRow);
            if (rows.Count == 0 && dgv_Tags.CurrentRow != null && !dgv_Tags.CurrentRow.IsNewRow)
                rows.Add(dgv_Tags.CurrentRow);
            return rows.Where(r => r != null && !r.IsNewRow && r.Index >= 0).GroupBy(r => r.Index).Select(g => g.First()).ToList();
        }

        private void EnsureQuickWriteUi()
        {
            if (quickWriteUiBuilt || splitContainer1?.Panel1 == null) return;
            splitContainer1.SplitterDistance = Math.Max(splitContainer1.SplitterDistance, 74);
            splitContainer1.Panel1.AutoScroll = true;

            lblQuickWriteValue = new Label { Name = "lblQuickWriteValue", AutoSize = true, Location = new Point(7, 44), Text = "快速写入:" };
            cmbQuickWriteValue = new ComboBox { Name = "cmbQuickWriteValue", Location = new Point(71, 40), Size = new Size(200, 25), DropDownStyle = ComboBoxStyle.DropDown };
            cmbQuickWriteValue.KeyDown += cmbQuickWriteValue_KeyDown;

            btnQuickUseCurrentValue = new Button { Name = "btnQuickUseCurrentValue", Location = new Point(278, 39), Size = new Size(78, 27), Text = "取当前值", FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
            btnQuickUseCurrentValue.Click += btnQuickUseCurrentValue_Click;

            btnQuickWriteSelected = new Button { Name = "btnQuickWriteSelected", Location = new Point(361, 39), Size = new Size(88, 27), Text = "写入选中", FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
            btnQuickWriteSelected.Click += btnQuickWriteSelected_Click;

            btnQuickWriteVerify = new Button { Name = "btnQuickWriteVerify", Location = new Point(454, 39), Size = new Size(112, 27), Text = "写入并校验", FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
            btnQuickWriteVerify.Click += btnQuickWriteVerify_Click;

            // CSV 导出按钮（值快照）
            _btnExportCsv = new Button { Name = "btnExportCsv", Location = new Point(572, 39), Size = new Size(78, 27), Text = "导出CSV", FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
            _btnExportCsv.Click += BtnExportCsv_Click;

            // CSV 导出/导入按钮（点表配置，非当前值）
            _btnExportCsvConfig = new Button { Name = "btnExportCsvConfig", Text = "CSV导出配置", FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Width = 96, Height = 27 };
            _btnExportCsvConfig.Click += BtnExportCsvConfig_Click;

            _btnImportCsvConfig = new Button { Name = "btnImportCsvConfig", Text = "CSV导入配置", FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Width = 96, Height = 27 };
            _btnImportCsvConfig.Click += BtnImportCsvConfig_Click;

            // 批量地址生成按钮
            _btnBatchGenerate = new Button { Name = "btnBatchGenerate", Text = "批量生成", FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Width = 78, Height = 27 };
            _btnBatchGenerate.Click += BtnBatchGenerate_Click;

            // 加载示例地址按钮
            _btnLoadDemo = new Button { Name = "btnLoadDemo", Text = "加载示例", FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Width = 78, Height = 27 };
            _btnLoadDemo.Click += BtnLoadDemo_Click;

            // 噪声过滤复选框（默认关闭）
            chkNoiseFilter = new CheckBox
            {
                Name = "chkNoiseFilter",
                Text = "降噪",
                AutoSize = true,
                Checked = false,
            };

            splitContainer1.Panel1.Controls.Add(lblQuickWriteValue);
            splitContainer1.Panel1.Controls.Add(cmbQuickWriteValue);
            splitContainer1.Panel1.Controls.Add(btnQuickUseCurrentValue);
            splitContainer1.Panel1.Controls.Add(btnQuickWriteSelected);
            splitContainer1.Panel1.Controls.Add(btnQuickWriteVerify);
            splitContainer1.Panel1.Controls.Add(_btnExportCsv);
            splitContainer1.Panel1.Controls.Add(_btnExportCsvConfig);
            splitContainer1.Panel1.Controls.Add(_btnImportCsvConfig);
            splitContainer1.Panel1.Controls.Add(_btnBatchGenerate);
            splitContainer1.Panel1.Controls.Add(_btnLoadDemo);
            splitContainer1.Panel1.Controls.Add(chkNoiseFilter);
            quickWriteUiBuilt = true;
            ApplyTopPanelLayout();
        }

        private void Panel1_SizeChanged(object sender, EventArgs e) { ApplyTopPanelLayout(); }

        private void ApplyTopPanelLayout()
        {
            if (splitContainer1?.Panel1 == null) return;

            var panel = splitContainer1.Panel1;
            int width = Math.Max(320, panel.ClientSize.Width);
            int padding = 7;
            int gap = 6;
            int rowHeight = 27;
            int row1Y = 6;
            int row2Y = row1Y + rowHeight + 6;

            // 第一行左侧：导入导出
            btn_out_clip.Location = new Point(padding, row1Y);
            btn_out_clip.Size = new Size(101, rowHeight);
            btn_from_clip.Location = new Point(btn_out_clip.Right + gap, row1Y);
            btn_from_clip.Size = new Size(101, rowHeight);
            btn_out_file.Location = new Point(btn_from_clip.Right + gap, row1Y);
            btn_out_file.Size = new Size(101, rowHeight);
            btn_from_file.Location = new Point(btn_out_file.Right + gap, row1Y);
            btn_from_file.Size = new Size(101, rowHeight);

            ck_AllTags.Location = new Point(btn_from_file.Right + gap + 2, row1Y + 3);
            ck_AllTags.AutoSize = true;

            // 日志降噪复选框（放在 ALL 右侧）
            chkNoiseFilter.Location = new Point(ck_AllTags.Right + gap, row1Y + 3);
            chkNoiseFilter.AutoSize = true;

            // 第一行右侧：清空、间隔、刷新
            int right = width - padding;
            btn_Refresh.Size = new Size(101, rowHeight);
            btn_Refresh.Location = new Point(right - btn_Refresh.Width, row1Y);
            right = btn_Refresh.Left - gap;

            textBox_time.Size = new Size(70, 23);
            textBox_time.Location = new Point(right - textBox_time.Width, row1Y + 2);
            right = textBox_time.Left - 4;

            label1.AutoSize = true;
            label1.Location = new Point(right - label1.PreferredWidth, row1Y + 5);
            right = label1.Left - gap;

            btn_ClearTagDgv.Size = new Size(78, rowHeight);
            btn_ClearTagDgv.Location = new Point(right - btn_ClearTagDgv.Width, row1Y);

            if (lblQuickWriteValue == null || cmbQuickWriteValue == null ||
                btnQuickUseCurrentValue == null || btnQuickWriteSelected == null || btnQuickWriteVerify == null)
            {
                return;
            }

            // 第二行：快速写入
            lblQuickWriteValue.AutoSize = true;
            lblQuickWriteValue.Location = new Point(padding, row2Y + 4);

            int row2Right = width - padding;
            btnQuickWriteVerify.Size = new Size(96, rowHeight);
            btnQuickWriteVerify.Location = new Point(row2Right - btnQuickWriteVerify.Width, row2Y);
            row2Right = btnQuickWriteVerify.Left - gap;

            if (_btnExportCsv != null)
            {
                _btnExportCsv.Size = new Size(78, rowHeight);
                _btnExportCsv.Location = new Point(row2Right - _btnExportCsv.Width, row2Y);
                row2Right = _btnExportCsv.Left - gap;
            }

            if (_btnExportCsvConfig != null)
            {
                _btnExportCsvConfig.Size = new Size(96, rowHeight);
                _btnExportCsvConfig.Location = new Point(row2Right - _btnExportCsvConfig.Width, row2Y);
                row2Right = _btnExportCsvConfig.Left - gap;
            }

            if (_btnImportCsvConfig != null)
            {
                _btnImportCsvConfig.Size = new Size(96, rowHeight);
                _btnImportCsvConfig.Location = new Point(row2Right - _btnImportCsvConfig.Width, row2Y);
                row2Right = _btnImportCsvConfig.Left - gap;
            }

            if (_btnBatchGenerate != null)
            {
                _btnBatchGenerate.Size = new Size(78, rowHeight);
                _btnBatchGenerate.Location = new Point(row2Right - _btnBatchGenerate.Width, row2Y);
                row2Right = _btnBatchGenerate.Left - gap;
            }

            if (_btnLoadDemo != null)
            {
                _btnLoadDemo.Size = new Size(78, rowHeight);
                _btnLoadDemo.Location = new Point(row2Right - _btnLoadDemo.Width, row2Y);
                row2Right = _btnLoadDemo.Left - gap;
            }

            btnQuickWriteSelected.Size = new Size(86, rowHeight);
            btnQuickWriteSelected.Location = new Point(row2Right - btnQuickWriteSelected.Width, row2Y);
            row2Right = btnQuickWriteSelected.Left - gap;

            btnQuickUseCurrentValue.Size = new Size(82, rowHeight);
            btnQuickUseCurrentValue.Location = new Point(row2Right - btnQuickUseCurrentValue.Width, row2Y);
            row2Right = btnQuickUseCurrentValue.Left - gap;

            int comboX = lblQuickWriteValue.Right + 6;
            int comboWidth = Math.Max(120, row2Right - comboX);
            cmbQuickWriteValue.Location = new Point(comboX, row2Y + 1);
            cmbQuickWriteValue.Size = new Size(comboWidth, 25);

            int targetHeight = row2Y + rowHeight + 8;
            if (splitContainer1.SplitterDistance != targetHeight)
            {
                splitContainer1.SplitterDistance = targetHeight;
            }
        }

        private void cmbQuickWriteValue_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = ExecuteQuickWriteAsync(false);
            }
        }

        private void btnQuickUseCurrentValue_Click(object sender, EventArgs e)
        {
            var rows = GetActionRows();
            if (rows.Count == 0)
            {
                MessageBox.Show("请先选中一行或多行。");
                return;
            }

            string val = rows[0].Cells[TagValCellIndex].Value?.ToString() ?? string.Empty;
            if (cmbQuickWriteValue != null)
            {
                cmbQuickWriteValue.Text = val;
            }
        }

        private async void btnQuickWriteSelected_Click(object sender, EventArgs e)
        {
            await ExecuteQuickWriteAsync(false);
        }

        private async void btnQuickWriteVerify_Click(object sender, EventArgs e)
        {
            await ExecuteQuickWriteAsync(true);
        }

        private async Task ExecuteQuickWriteAsync(bool verifyAfterWrite)
        {
            var selectedRows = GetActionRows();
            if (selectedRows.Count == 0)
            {
                MessageBox.Show("请先选中需要写入的行。");
                return;
            }

            if (device == null)
            {
                MessageBox.Show("设备为断开或未连接，无法写入!");
                return;
            }

            string value = GetQuickWriteInputValue();
            if (string.IsNullOrWhiteSpace(value))
            {
                MessageBox.Show("请输入要写入的值。");
                return;
            }

            AppendQuickWriteHistory(value);

            dgv_Tags.Cursor = Cursors.WaitCursor;
            WriteSummary summary;
            try
            {
                summary = await WriteRowsAsync(selectedRows, _ => value);
            }
            finally
            {
                dgv_Tags.Cursor = Cursors.Default;
            }

            if (verifyAfterWrite)
            {
                var verify = await VerifyRowsAsync(selectedRows, value);
                string verifyDetail = $"校验成功 {verify.OkCount}，不一致 {verify.MismatchCount}，读失败 {verify.ReadFailedCount}";
                if (verify.Messages.Count > 0)
                {
                    verifyDetail += Environment.NewLine + string.Join(Environment.NewLine, verify.Messages.Take(5));
                }

                MessageBox.Show($"写入完成：成功 {summary.SuccessCount}，失败 {summary.FailedCount}{Environment.NewLine}{verifyDetail}");
            }
            else
            {
                if (summary.FailedCount > 0)
                {
                    string preview = string.Join(Environment.NewLine, summary.Errors.Take(5));
                    MessageBox.Show($"写入完成：成功 {summary.SuccessCount}，失败 {summary.FailedCount}{Environment.NewLine}{preview}");
                }
                else
                {
                    MessageBox.Show($"写入完成：成功 {summary.SuccessCount}");
                }
            }
        }

        private string GetQuickWriteInputValue()
        {
            if (cmbQuickWriteValue == null) return string.Empty;
            return (cmbQuickWriteValue.Text ?? string.Empty).Trim();
        }

        private void AppendQuickWriteHistory(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized) || cmbQuickWriteValue == null) return;

            quickWriteHistory.RemoveAll(v => string.Equals(v, normalized, StringComparison.Ordinal));
            quickWriteHistory.Insert(0, normalized);

            if (quickWriteHistory.Count > QuickWriteHistoryLimit)
            {
                quickWriteHistory.RemoveRange(QuickWriteHistoryLimit, quickWriteHistory.Count - QuickWriteHistoryLimit);
            }

            cmbQuickWriteValue.BeginUpdate();
            try
            {
                cmbQuickWriteValue.Items.Clear();
                cmbQuickWriteValue.Items.AddRange(quickWriteHistory.Cast<object>().ToArray());
                cmbQuickWriteValue.Text = normalized;
            }
            finally
            {
                cmbQuickWriteValue.EndUpdate();
            }
        }

        private sealed class VerifySummary
        {
            public int OkCount { get; set; }
            public int MismatchCount { get; set; }
            public int ReadFailedCount { get; set; }
            public List<string> Messages { get; } = new List<string>();
        }

        private async Task<VerifySummary> VerifyRowsAsync(IReadOnlyList<DataGridViewRow> rows, string expectedValue)
        {
            var result = new VerifySummary();
            foreach (var row in rows)
            {
                if (row == null || row.IsNewRow) continue;
                TagItem tag = GetTagItem(row);
                string actual = _batchMonitor != null ? await _batchMonitor.ReadSingleValueAsync(tag) : null;
                if (actual == null)
                {
                    result.ReadFailedCount++;
                    result.Messages.Add($"读失败: {tag.Address}");
                    continue;
                }

                if (AreValuesEquivalent(tag, expectedValue, actual))
                {
                    result.OkCount++;
                }
                else
                {
                    result.MismatchCount++;
                    result.Messages.Add($"不一致: {tag.Address} 预期[{expectedValue}] 实际[{actual}]");
                }
            }
            return result;
        }

        private bool AreValuesEquivalent(TagItem tag, string expectedRaw, string actualRaw)
        {
            if (tag == null) return string.Equals(expectedRaw?.Trim(), actualRaw?.Trim(), StringComparison.OrdinalIgnoreCase);
            string type = (tag.DataTypeCode ?? string.Empty).Trim().ToLowerInvariant();
            string expected = (expectedRaw ?? string.Empty).Trim();
            string actual = (actualRaw ?? string.Empty).Trim();

            switch (type)
            {
                case "bool":
                case "bit":
                    if (TryParseBoolFlexible(expected, out bool eb) && TryParseBoolFlexible(actual, out bool ab))
                        return eb == ab;
                    break;
                case "byte":
                    if (TryParseByteFlexible(expected, out byte eby, true) && TryParseByteFlexible(actual, out byte aby, true))
                        return eby == aby;
                    break;
                case "short":
                    if (TryParseInt16Flexible(expected, out short es) && TryParseInt16Flexible(actual, out short @as))
                        return es == @as;
                    break;
                case "ushort":
                    if (TryParseUInt16Flexible(expected, out ushort eus) && TryParseUInt16Flexible(actual, out ushort aus))
                        return eus == aus;
                    break;
                case "int":
                    if (TryParseInt32Flexible(expected, out int ei) && TryParseInt32Flexible(actual, out int ai))
                        return ei == ai;
                    break;
                case "uint":
                    if (TryParseUInt32Flexible(expected, out uint eui) && TryParseUInt32Flexible(actual, out uint aui))
                        return eui == aui;
                    break;
                case "long":
                    if (TryParseInt64Flexible(expected, out long el) && TryParseInt64Flexible(actual, out long al))
                        return el == al;
                    break;
                case "ulong":
                    if (TryParseUInt64Flexible(expected, out ulong eul) && TryParseUInt64Flexible(actual, out ulong aul))
                        return eul == aul;
                    break;
                case "float":
                    if (TryParseSingleFlexible(expected, out float ef) && TryParseSingleFlexible(actual, out float af))
                        return Math.Abs(ef - af) <= Math.Max(1e-5f, Math.Abs(ef) * 1e-4f);
                    break;
                case "double":
                    if (TryParseDoubleFlexible(expected, out double ed) && TryParseDoubleFlexible(actual, out double ad))
                        return Math.Abs(ed - ad) <= Math.Max(1e-8, Math.Abs(ed) * 1e-6);
                    break;
                case "byte[]":
                    if (TryParseByteArray(expected, out byte[] eba, out _) && TryParseByteArray(actual, out byte[] aba, out _))
                        return eba.SequenceEqual(aba);
                    break;
                case "string":
                    return string.Equals(expected, actual, StringComparison.Ordinal);
            }

            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region dgv_Tags 控件的大小更改时调整列的宽度
        private void dgv_Tags_SizeChanged(object sender, EventArgs e)
        {
            AdjustColumnWidth();
        }

        private void AdjustColumnWidth()
        {
            if (dgv_Tags.Width < 1200) return;
            int totalWidth = dgv_Tags.ClientSize.Width;

            // 计算其他列的总宽度（排除 TagValCellIndex 列）
            int otherColumnsWidth = 0;
            for (int i = 0; i < dgv_Tags.Columns.Count; i++)
            {
                if (i != TagValCellIndex)
                    otherColumnsWidth += dgv_Tags.Columns[i].Width;
            }

            // 计算 TagValCellIndex 列的宽度
            int tagValColumnWidth = totalWidth - otherColumnsWidth;
            if (tagValColumnWidth > 0)
                dgv_Tags.Columns[TagValCellIndex].Width = tagValColumnWidth;
        }
        #endregion

        #region 返回TagItem 也就是一行数据项
        private TagItem GetTagItem(DataGridViewRow dgvr)
        {
            TagItem tagItem = new TagItem();
            tagItem.Enable = dgvr.Cells[0].Value != null ? bool.Parse(dgvr.Cells[0].Value.ToString()) : true;
            tagItem.Id = dgvr.Cells[1].Value != null ? dgvr.Cells[1].Value.ToString() : string.Empty;
            tagItem.Address = dgvr.Cells[TagAddressCellIndex].Value != null ? dgvr.Cells[TagAddressCellIndex].Value.ToString() : string.Empty;
            tagItem.DataTypeCode = dgvr.Cells[3].Value != null ? dgvr.Cells[3].Value.ToString() : string.Empty;
            if (tagItem.DataTypeCode == "string")
            {
                if (dgvr.Cells[4].Value != null)
                    tagItem.StringEncoding = dgvr.Cells[4].Value.ToString();
                else
                    tagItem.StringEncoding = "ASCII";
            }
            string length_str = dgvr.Cells[5].Value != null ? dgvr.Cells[5].Value.ToString() : string.Empty;
            tagItem.Length = string.IsNullOrEmpty(length_str) ? -1 : Convert.ToInt32(length_str);
            tagItem.Unit = dgvr.Cells[7].Value != null ? dgvr.Cells[7].Value.ToString() : string.Empty;
            tagItem.Description = dgvr.Cells[8].Value != null ? dgvr.Cells[8].Value.ToString() : string.Empty;
            return tagItem;
        }
        #endregion

        #region 遍历 dgv_Tags 并生成 XML
        public void GetDataTable(XElement element)
        {
            element.RemoveNodes();
            for (int i = 0; i < dgv_Tags.Rows.Count; i++)
            {
                DataGridViewRow dgvr = dgv_Tags.Rows[i];
                if (dgvr.Cells[TagAddressCellIndex].Value == null) continue;
                if (dgvr.IsNewRow) continue;

                TagItem tagItem = GetTagItem(dgvr);
                element.Add(tagItem.ToXmlElement());
            }
        }
        #endregion

        #region 从 XML 加载数据
        public int LoadDataTable(XElement element)
        {
            int count = 0;
            foreach (var item in element.Elements())
            {
                if (item.Name == nameof(TagItem))
                {
                    TagItem tagItem = new TagItem();
                    tagItem.LoadByXmlElement(item);

                    int rowIndex = dgv_Tags.Rows.Add();
                    DataGridViewRow dgvr = dgv_Tags.Rows[rowIndex];

                    dgvr.Cells[0].Value = tagItem.Enable;
                    dgvr.Cells[1].Value = tagItem.Id;
                    dgvr.Cells[TagAddressCellIndex].Value = tagItem.Address;
                    if (!string.IsNullOrEmpty(tagItem.DataTypeCode))
                    {
                        dgvr.Cells[3].Value = tagItem.DataTypeCode;
                        if (tagItem.DataTypeCode == "string")
                        {
                            dgvr.Cells[4].Value = tagItem.StringEncoding.ToString();
                        }
                    }
                    if (tagItem.Length >= 0)
                    {
                        dgvr.Cells[5].Value = tagItem.Length.ToString();
                    }
                    dgvr.Cells[7].Value = tagItem.Unit;
                    dgvr.Cells[8].Value = tagItem.Description;

                    count++;
                }
            }
            RequestUpdateTagItemList(true);
            return count;
        }
        #endregion

        private int timeSleep = 100;

        #region 开始/停止刷新数据
        private void btn_Refresh_Click(object sender, EventArgs e)
        {
            if (IsRefreshing)
            {
                cancellationTokenSource?.Cancel();
            }
            else
            {
                TryStartRefresh(false, out _);
            }
        }

        public void ConnectClose()
        {
            Stop();
            _batchMonitor?.Dispose();
            _batchMonitor = null;
            this.device = null;
        }

        public void Stop()
        {
            cancellationTokenSource?.Cancel();
            setRefreshEnable(false);
        }

        public void setRefreshEnable(bool enable)
        {
            this.btn_Refresh.Enabled = enable;
        }
        #endregion

        #region 刷新数据的线程 (核心重构：后台批处理读取，单次集中刷UI)

        /// <summary>
        /// 后台采集循环——核心数据流。
        /// 
        /// 运行方式：在 Task.Run 的后台线程中持续运行，通过 CancellationToken 控制退出。
        /// 
        /// 循环流程：
        /// 1. 检查缓存和 BatchMonitor 是否就绪
        /// 2. BatchMonitor.BuildPlan：将所有标签按地址分组、合并相邻地址、生成最优读取计划
        /// 3. BatchMonitor.ExecuteReadCycleAsync：执行批量读取（一次网络通讯读多个标签）
        /// 4. BeginInvoke 投递到 UI 线程更新 DataGridView
        /// 5. 计算本轮耗时，调整 Task.Delay 使周期 timeSleep 恒定
        ///    （固定频率而非固定间隔：比如设 200ms，不管读取花了多久，都等够 200ms 再下一轮）
        /// 
        /// 性能设计：
        /// - BeginInvoke 而非 Invoke：背景采集线程不阻塞等待 UI 渲染，直接进入下一轮
        /// - 只更新值变化的单元格（currentUiVal != newVal），不刷新整行
        /// - SuspendLayout/ResumeLayout 包裹批量更新，减少 DataGridView 重绘次数
        /// - 周期时间在循环结束时校准（Stopwatch 计时 + Task.Delay 补齐），不漂移
        /// 
        /// 线程安全：
        /// - 字典 tagDgvRowDic 只读不写（后台只查找，不修改）
        /// - 所有 DataGridView 操作在 BeginInvoke 闭包中，确保在 UI 线程执行
        /// - IsDisposed 检查防止控件已销毁后还在更新
        /// </summary>
        private async Task RefreshDataAsync(CancellationToken cancellationToken)
        {
            var period = TimeSpan.FromMilliseconds(timeSleep);
            while (!cancellationToken.IsCancellationRequested)
            {
                // 标签列表为空或 BatchMonitor 未就绪时降频等待（10 倍间隔）
                if (tagDgvRowDic.Count == 0 || _batchMonitor == null)
                {
                    await Task.Delay(timeSleep * 10, cancellationToken);
                    continue;
                }

                var cycleStart = Stopwatch.StartNew();

                // Step 1: 构建批量读取计划——将多个标签地址合并为最少的通讯请求
                _batchMonitor.BuildPlan(tagDgvRowDic.Values);

                // Step 2: 执行批量读取——真正的 PLC 通讯操作
                var results = await _batchMonitor.ExecuteReadCycleAsync(cancellationToken);

                if (cancellationToken.IsCancellationRequested || this.IsDisposed) break;

                // Step 3: 异步投递 UI 更新（BeginInvoke 不阻塞后台线程）
                this.BeginInvoke(new Action(() =>
                {
                    if (this.IsDisposed) return;
                    dgv_Tags.SuspendLayout();
                    foreach (var result in results)
                    {
                        if (!result.IsSuccess) continue;
                        string tagKey = BatchMonitor.BuildTagKey(result.Tag);
                        var row = tagDgvRowDic.FirstOrDefault(kv =>
                            BatchMonitor.BuildTagKey(kv.Value) == tagKey).Key;
                        if (row == null || row.DataGridView == null) continue;

                        string newVal = result.DisplayValue;
                        string currentUiVal = row.Cells[TagValCellIndex].Value as string ?? string.Empty;
                        // 只在值发生变化时更新单元格，减少不必要地重绘
                        if (currentUiVal != newVal)
                        {
                            row.Cells[TagValCellIndex].Value = newVal;
                            SetLogs(newVal, result.Tag.Address);

                            // 触发趋势图事件，供 UcTrendPanel 消费
                            OnValueCollected?.Invoke(result.Tag.Address, newVal, result.Tag.DataTypeCode);
                        }
                    }
                    dgv_Tags.ResumeLayout();
                }));

                // Step 4: 校准周期——减去采集本身的时间，使两次读取间保持固定间隔
                var elapsed = ZL.Iot.Controls.Common.TimeHelpers.GetElapsedSince(cycleStart);
                var delay = period - elapsed;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);
            }
        }
        #endregion

        /// <summary>
        /// 记录值变化日志（带防重复过滤 + 可选噪声过滤）。
        /// 
        /// 防重复过滤：同一个标签地址的连续相同值只输出一次日志。
        /// 噪声过滤（chkNoiseFilter 启用时）：对于数值类型，只有变化幅度超过 1%
        /// 或绝对值超过 0.5 时才输出日志。bool/string 等非数值类型始终输出。
        /// 
        /// 使用 ConcurrentDictionary 确保后台线程安全写入。
        /// </summary>
        private void SetLogs(string newVal, string CacheKey)
        {
            if (string.IsNullOrEmpty(CacheKey)) return;
            valueChangeCache.TryGetValue(CacheKey, out string? oldVal);
            if (oldVal == null || !newVal.Equals(oldVal))
            {
                // 噪声过滤：启用时，小幅度数值变化不输出日志
                if (chkNoiseFilter != null && chkNoiseFilter.Checked)
                {
                    if (IsSmallNumericChange(oldVal, newVal))
                    {
                        // 只更新缓存不输出日志
                        valueChangeCache.AddOrUpdate(CacheKey, newVal, (_, __) => newVal);
                        return;
                    }
                }

                valueChangeCache.AddOrUpdate(CacheKey, newVal, (_, __) => newVal);
                OnLogs?.Invoke($" 采集【{CacheKey}】标签值为：{newVal}");
            }
        }

        /// <summary>
        /// 判断数值变化是否过小（需要过滤的"噪声"）。
        /// 先尝试解析为 double，比较相对变化 (&lt;1%) 和绝对变化 (&lt;0.5)。
        /// 解析失败（非数值类型）返回 false，不过滤。
        /// </summary>
        private static bool IsSmallNumericChange(string oldVal, string newVal)
        {
            if (string.IsNullOrEmpty(oldVal) || string.IsNullOrEmpty(newVal))
                return false;
            if (!double.TryParse(oldVal, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out double oldNum))
                return false;
            if (!double.TryParse(newVal, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out double newNum))
                return false;

            double diff = Math.Abs(newNum - oldNum);
            double avg = (Math.Abs(oldNum) + Math.Abs(newNum)) / 2;
            // 变化 < 1% 且绝对值 < 0.5 时视为噪声
            return diff < 0.5 && (avg < 1e-10 || diff / avg < 0.01);
        }

        private void BtnExportCsv_Click(object sender, EventArgs e)
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "CSV 文件|*.csv",
                FileName = $"tag_values_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = "csv",
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;
            try
            {
                using var sw = new StreamWriter(sfd.FileName, false, Encoding.UTF8);
                sw.WriteLine("Address,DataType,Value,Unit,Description");
                foreach (DataGridViewRow row in dgv_Tags.Rows)
                {
                    if (row.IsNewRow) continue;
                    string addr = CsvEscape(row.Cells[TagAddressCellIndex].Value?.ToString());
                    string type = CsvEscape(row.Cells[3].Value?.ToString());
                    string val = CsvEscape(row.Cells[TagValCellIndex].Value?.ToString());
                    string unit = CsvEscape(row.Cells[7].Value?.ToString());
                    string desc = CsvEscape(row.Cells[8].Value?.ToString());
                    sw.WriteLine($"{addr},{type},{val},{unit},{desc}");
                }
                OnLogs?.Invoke($"CSV 导出成功: {sfd.FileName}");
            }
            catch (Exception ex) { MessageBox.Show($"CSV 导出失败: {ex.Message}", "导出错误"); }
        }

        private static string CsvEscape(string? val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            if (val.Contains(',') || val.Contains('"') || val.Contains('\n'))
                return $"\"{val.Replace("\"", "\"\"")}\"";
            return val;
        }

        /// <summary>
        /// 导出点表配置为 CSV（地址、类型、编码、长度、单位、注释）。
        /// 不包含当前值——只导配置，方便用 Excel 编辑后重新导入。
        /// CSV 首行为列标题，编码 UTF-8 BOM 确保 Excel 正确识别中文。
        /// </summary>
        private void BtnExportCsvConfig_Click(object? sender, EventArgs e)
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "CSV 文件|*.csv",
                FileName = $"tag_config_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = "csv",
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            try
            {
                using var sw = new StreamWriter(sfd.FileName, false, new UTF8Encoding(true)); // UTF-8 BOM
                sw.WriteLine("Enable,Name,Address,DataType,Encoding,Length,Unit,Description");
                foreach (DataGridViewRow row in dgv_Tags.Rows)
                {
                    if (row.IsNewRow) continue;
                    string enable = row.Cells[0].Value is bool b ? (b ? "1" : "0") : "1";
                    string name = CsvEscape(row.Cells[1].Value?.ToString());
                    string addr = CsvEscape(row.Cells[TagAddressCellIndex].Value?.ToString());
                    string type = CsvEscape(row.Cells[3].Value?.ToString());
                    string encoding = CsvEscape(row.Cells[4].Value?.ToString());
                    string length = CsvEscape(row.Cells[5].Value?.ToString());
                    string unit = CsvEscape(row.Cells[7].Value?.ToString());
                    string desc = CsvEscape(row.Cells[8].Value?.ToString());
                    sw.WriteLine($"{enable},{name},{addr},{type},{encoding},{length},{unit},{desc}");
                }
                OnLogs?.Invoke($"点表配置已导出: {sfd.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", "错误");
            }
        }

        /// <summary>
        /// 从 CSV 文件导入点表配置。
        /// 兼容 Excel 导出的 UTF-8 BOM CSV。
        /// 首行必须为列标题行，自动跳过。
        /// 格式：Enable,Name,Address,DataType,Encoding,Length,Unit,Description
        /// </summary>
        private void BtnImportCsvConfig_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "CSV 文件|*.csv|所有文件|*.*",
                Title = "导入点表配置（CSV）",
            };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            try
            {
                var lines = System.IO.File.ReadAllLines(ofd.FileName, Encoding.UTF8);
                if (lines.Length < 2)
                {
                    MessageBox.Show("CSV 文件为空或只有标题行", "导入失败");
                    return;
                }

                // 跳过标题行（第 0 行）
                int imported = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    // 解析 CSV 行（支持引号内逗号）
                    var fields = ParseCsvLine(line);
                    if (fields.Count < 3) continue; // 最少需要 Enable/Name/Address

                    // 确保有足够的列
                    while (fields.Count < 8) fields.Add("");

                    int rowIndex = dgv_Tags.Rows.Add();
                    var row = dgv_Tags.Rows[rowIndex];

                    // Enable
                    row.Cells[0].Value = fields[0] == "1" || fields[0].ToLowerInvariant() == "true";
                    // Name
                    row.Cells[1].Value = fields[1];
                    // Address
                    row.Cells[TagAddressCellIndex].Value = fields[2];
                    // DataType
                    if (!string.IsNullOrEmpty(fields[3]))
                    {
                        row.Cells[3].Value = fields[3];
                        // 如果是 string 类型，自动设置编码
                        if (fields[3].Trim().ToLowerInvariant() == "string" && !string.IsNullOrEmpty(fields[4]))
                            row.Cells[4].Value = fields[4];
                    }
                    // Length
                    if (!string.IsNullOrEmpty(fields[5]))
                        row.Cells[5].Value = fields[5];
                    // Unit
                    row.Cells[7].Value = fields[6];
                    // Description
                    row.Cells[8].Value = fields[7];
                    imported++;
                }

                RequestUpdateTagItemList(true);
                OnLogs?.Invoke($"CSV 导入完成: {imported} 个标签");
                MessageBox.Show($"成功导入 {imported} 个标签", "导入完成");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败: {ex.Message}", "错误");
            }
        }

        /// <summary>
        /// 解析 CSV 单行，支持引号内嵌逗号和换行转义。
        /// </summary>
        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    // 连续两个引号表示转义引号
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++; // 跳过下一个引号
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            fields.Add(current.ToString()); // 最后一列
            return fields;
        }

        /// <summary>
        /// 批量地址生成对话框。
        /// 用户设置起始地址、数量、步长、数据类型、名称模板，一键生成多行。
        /// 
        /// 示例：
        ///   起始地址: DB1.DBD0   数量: 10   步长: 4   类型: Real   名称: Tag{0}
        ///   → 生成 DB1.DBD0, DB1.DBD4, DB1.DBD8, ... DB1.DBD36
        /// </summary>
        private void BtnBatchGenerate_Click(object? sender, EventArgs e)
        {
            using var form = new Form
            {
                Text = "批量生成地址",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(480, 260),
            };

            var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 3, RowCount = 6 };
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            // 行0: 起始地址
            tbl.Controls.Add(new Label { Text = "起始地址:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            var txtStartAddr = new TextBox { Text = "DB1.DBD0", Dock = DockStyle.Fill };
            tbl.Controls.Add(txtStartAddr, 1, 0);
            tbl.SetColumnSpan(tbl.GetControlFromPosition(1, 1), 2);

            // 行1: 数量
            tbl.Controls.Add(new Label { Text = "生成数量:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            var numCount = new NumericUpDown { Minimum = 1, Maximum = 500, Value = 10, Width = 100 };
            tbl.Controls.Add(numCount, 1, 1);

            // 行2: 步长(字节)
            tbl.Controls.Add(new Label { Text = "字节步长:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            var numStep = new NumericUpDown { Minimum = 1, Maximum = 256, Value = 4, Width = 100 };
            tbl.Controls.Add(numStep, 1, 2);

            // 行3: 数据类型
            tbl.Controls.Add(new Label { Text = "数据类型:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            var cmbType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            cmbType.Items.AddRange(new[] { "bool", "byte", "short", "ushort", "int", "uint", "float", "double", "string" });
            cmbType.SelectedIndex = 6; // float
            tbl.Controls.Add(cmbType, 1, 3);

            // 行4: 名称模板
            tbl.Controls.Add(new Label { Text = "名称模板:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
            var txtNameTmpl = new TextBox { Text = "Tag{0}", Dock = DockStyle.Fill };
            tbl.Controls.Add(txtNameTmpl, 1, 4);

            // 行5: 按钮
            var btnOk = new Button { Text = "生成", DialogResult = DialogResult.OK, Width = 80, Height = 30, FlatStyle = FlatStyle.Flat };
            var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80, Height = 30, FlatStyle = FlatStyle.Flat };
            var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            btnPanel.Controls.Add(btnCancel);
            btnPanel.Controls.Add(btnOk);
            tbl.Controls.Add(btnPanel, 1, 5);
            tbl.SetColumnSpan(btnPanel, 2);

            form.Controls.Add(tbl);
            form.AcceptButton = btnOk;
            form.CancelButton = btnCancel;

            if (form.ShowDialog(this) != DialogResult.OK) return;

            // 解析起始地址
            string startAddr = txtStartAddr.Text.Trim();
            int count = (int)numCount.Value;
            int step = (int)numStep.Value;
            string dataType = cmbType.Text;
            string nameTmpl = txtNameTmpl.Text.Trim();
            if (string.IsNullOrEmpty(nameTmpl)) nameTmpl = "Tag{0}";

            // 检查地址格式必须包含数字后缀（如 DBD10、DBW20、DBX0.0）
            // 提取数字后缀做加法，前缀保持不变
            string addrPrefix = startAddr;
            int addrNumStart = startAddr.Length;
            // 从末尾向前找数字开始位置
            for (int i = startAddr.Length - 1; i >= 0; i--)
            {
                if (!char.IsDigit(startAddr[i]) && startAddr[i] != '.')
                {
                    addrNumStart = i + 1;
                    break;
                }
                if (i == 0) addrNumStart = 0;
            }
            string prefix = startAddr.Substring(0, addrNumStart);
            if (!int.TryParse(startAddr.Substring(addrNumStart), out int startNum))
            {
                MessageBox.Show("起始地址末尾需要数字，如 DB1.DBD0", "格式错误");
                return;
            }

            dgv_Tags.SuspendLayout();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    int addrNum = startNum + i * step;
                    int ri = dgv_Tags.Rows.Add();
                    var row = dgv_Tags.Rows[ri];
                    row.Cells[0].Value = true;              // Enable
                    row.Cells[1].Value = string.Format(nameTmpl, i + 1); // Name
                    row.Cells[TagAddressCellIndex].Value = $"{prefix}{addrNum}"; // Address
                    row.Cells[3].Value = dataType;            // DataType
                }
                RequestUpdateTagItemList(true);
                OnLogs?.Invoke($"批量生成完成: {count} 个标签 ({dataType})");
            }
            finally
            {
                dgv_Tags.ResumeLayout();
            }
        }

        /// <summary>
        /// 加载当前协议的示例地址到点表。
        /// 每种数据类型配一条示例，帮助用户快速上手地址格式。
        /// 示例数据来自 DemoAddresses 静态字典（按 ProtocolType 索引）。
        /// 如果当前协议没有对应的示例数据，尝试用默认协议名查找。
        /// </summary>
        private void BtnLoadDemo_Click(object? sender, EventArgs e)
        {
            // 按 ProtocolType (ProtocolCatalog.CanonicalId) 查找示例数据
            var key = (ProtocolType ?? "siemens-s7").Trim();
            if (!DemoAddresses.TryGetValue(key, out var demos))
            {
                MessageBox.Show($"未找到 {ProtocolType} 的示例地址", "提示");
                return;
            }

            // 确认是否清空已有点表
            if (dgv_Tags.Rows.Count > 0)
            {
                var result = MessageBox.Show(
                    "加载示例将清空当前点表，是否继续？", "加载示例",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result != DialogResult.Yes) return;
                dgv_Tags.Rows.Clear();
            }

            dgv_Tags.SuspendLayout();
            try
            {
                foreach (var (name, address, type) in demos)
                {
                    int ri = dgv_Tags.Rows.Add();
                    var row = dgv_Tags.Rows[ri];
                    row.Cells[0].Value = true;       // Enable
                    row.Cells[1].Value = name;        // Name
                    row.Cells[TagAddressCellIndex].Value = address; // Address
                    row.Cells[3].Value = type;        // DataType
                }
                RequestUpdateTagItemList(true);
                OnLogs?.Invoke($"已加载 {ProtocolType} 示例地址 ({demos.Length} 条)");
            }
            finally
            {
                dgv_Tags.ResumeLayout();
            }
        }

        #region 按钮事件
        private void button_out_clip_Click(object sender, EventArgs e)
        {
            XElement element = new XElement("DataTable");
            GetDataTable(element);
            Clipboard.SetText(element.ToString());
            MessageBox.Show("保存成功!");
        }

        private void button_from_clip_Click(object sender, EventArgs e)
        {
            try
            {
                XElement element = XElement.Parse(Clipboard.GetText());
                LoadDataTable(element);
            }
            catch (Exception ex)
            {
                MessageBox.Show("加载失败: " + ex.Message);
            }
        }

        bool IsDataGridViewEmpty(DataGridView dataGridView)
        {
            if (dataGridView.Rows.Count == 0) return true;

            foreach (DataGridViewRow row in dataGridView.Rows)
            {
                if (!row.IsNewRow) return false;
                if (row.Cells[TagAddressCellIndex].Value != null && !string.IsNullOrEmpty(row.Cells[TagAddressCellIndex].Value.ToString().Trim()))
                    return false;
            }
            return true;
        }

        private void button_out_file_Click(object sender, EventArgs e)
        {
            if (IsDataGridViewEmpty(dgv_Tags))
            {
                MessageBox.Show("点表为空，无法导出！");
                return;
            }
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "*XML|*.xml";
                sfd.FileName = FileName;
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    XElement element = new XElement("DataTable");
                    GetDataTable(element);
                    element.Save(sfd.FileName);
                    MessageBox.Show("导出成功!");
                }
            }
        }

        private void button_from_file_Click(object sender, EventArgs e)
        {
            try
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "*XML|*.xml";
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        XElement element = XElement.Parse(File.ReadAllText(ofd.FileName, Encoding.UTF8));
                        LoadDataTable(element);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("加载失败: " + ex.Message);
            }
        }

        private void btn_ClearTagDgv_Click(object sender, EventArgs e)
        {
            Stop();
            dgv_Tags.Rows.Clear();
            RequestUpdateTagItemList(true);
        }

        private async void ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (!(sender is ToolStripMenuItem menuItem)) return;

                var selectedRows = GetActionRows();

                if (menuItem.Name == "TSMI_del")
                {
                    DeleteRowsSafely(selectedRows);
                    RequestUpdateTagItemList(true);
                    return;
                }

                if (menuItem.Name == "TSMI_Save")
                {
                    MessageBox.Show("保存功能预留，当前请使用上方导出按钮。");
                    return;
                }

                if (selectedRows.Count == 0)
                {
                    MessageBox.Show("未选中可操作行。");
                    return;
                }

                if (device == null)
                {
                    MessageBox.Show("设备为断开或未连接，无法写入!");
                    return;
                }

                Func<DataGridViewRow, string> valueFactory;
                if (menuItem.Name == "TSMI_WriteBatchSameValue")
                {
                    string defaultValue = selectedRows[0].Cells[TagValCellIndex].Value?.ToString() ?? string.Empty;
                    if (!ShowInputDialog("批量同值写入", "请输入要写入到选中点位的值：", defaultValue, out string sameValue))
                    {
                        return;
                    }
                    valueFactory = _ => sameValue;
                }
                else if (menuItem.Name == "TSMI_Write")
                {
                    bool allBlank = selectedRows.All(r => string.IsNullOrWhiteSpace(r.Cells[TagValCellIndex].Value?.ToString()));
                    if (allBlank)
                    {
                        if (!ShowInputDialog("写入值", "当前值列为空，请输入写入值：", string.Empty, out string manualValue))
                        {
                            return;
                        }
                        valueFactory = _ => manualValue;
                    }
                    else
                    {
                        valueFactory = r => r.Cells[TagValCellIndex].Value?.ToString() ?? string.Empty;
                    }
                }
                else
                {
                    return;
                }

                dgv_Tags.Cursor = Cursors.WaitCursor;
                WriteSummary summary;
                try
                {
                    summary = await WriteRowsAsync(selectedRows, valueFactory);
                }
                finally
                {
                    dgv_Tags.Cursor = Cursors.Default;
                }

                if (summary.FailedCount > 0)
                {
                    string preview = string.Join(Environment.NewLine, summary.Errors.Take(5));
                    MessageBox.Show($"写入完成：成功 {summary.SuccessCount}，失败 {summary.FailedCount}{Environment.NewLine}{preview}");
                }
                else
                {
                    MessageBox.Show($"写入完成：成功 {summary.SuccessCount}");
                }

                RequestUpdateTagItemList(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"写入操作异常: {ex.Message}", "错误");
            }
        }
        #endregion

        private async Task<WriteSummary> WriteRowsAsync(IReadOnlyList<DataGridViewRow> rows, Func<DataGridViewRow, string> valueFactory)
        {
            var summary = new WriteSummary();

            foreach (var row in rows)
            {
                if (row == null || row.IsNewRow) continue;

                TagItem tagItem = GetTagItem(row);
                string rawValue = valueFactory?.Invoke(row) ?? string.Empty;
                string encodingStr = row.Cells[4].Value?.ToString() ?? "ASCII";

                var singleResult = await Task.Run(() => TryWriteTagValue(tagItem, rawValue, encodingStr, out string message)
                    ? $"OK:{message}"
                    : $"FAIL:{message}");

                if (singleResult.StartsWith("OK:", StringComparison.Ordinal))
                {
                    summary.SuccessCount++;
                    OnLogs?.Invoke($"写入成功【{tagItem.Address}】值：{rawValue}");
                }
                else
                {
                    summary.FailedCount++;
                    string err = singleResult.Substring("FAIL:".Length);
                    string line = $"{tagItem.Address}({tagItem.DataTypeCode}) -> {err}";
                    summary.Errors.Add(line);
                    OnLogs?.Invoke($"写入失败【{tagItem.Address}】：{err}");
                }
            }

            return summary;
        }

        private bool TryWriteTagValue(TagItem tagItem, string rawValue, string encodingStr, out string message)
        {
            message = string.Empty;
            if (device == null)
            {
                message = "设备未连接";
                return false;
            }

            if (tagItem == null)
            {
                message = "点位为空";
                return false;
            }

            if (string.IsNullOrWhiteSpace(tagItem.Address))
            {
                message = "地址为空";
                return false;
            }

            if (string.IsNullOrWhiteSpace(tagItem.DataTypeCode))
            {
                message = "数据类型为空";
                return false;
            }

            try
            {
                if (!TryParseWriteValue(tagItem, rawValue, out object typedValue, out string parseError))
                {
                    message = parseError;
                    return false;
                }

                DeviceResult writeResult = ExecuteDeviceWrite(tagItem, typedValue, encodingStr);
                if (writeResult != null && !writeResult.IsSuccess)
                {
                    message = string.IsNullOrWhiteSpace(writeResult.Message) ? "设备返回写入失败" : writeResult.Message;
                    return false;
                }

                message = "写入成功";
                return true;
            }
            catch (Exception ex)
            {
                message = $"写入异常: {ex.Message}";
                return false;
            }
        }

        private bool TryParseWriteValue(TagItem tagItem, string rawValue, out object typedValue, out string error)
        {
            typedValue = null;
            error = string.Empty;

            string dataType = (tagItem.DataTypeCode ?? string.Empty).Trim().ToLowerInvariant();
            string text = (rawValue ?? string.Empty).Trim();

            switch (dataType)
            {
                case "bool":
                case "bit":
                    if (TryParseBoolFlexible(text, out bool boolValue))
                    {
                        typedValue = boolValue;
                        return true;
                    }
                    error = $"bool格式错误: {text}";
                    return false;

                case "byte":
                    if (TryParseByteFlexible(text, out byte byteValue, false))
                    {
                        typedValue = byteValue;
                        return true;
                    }
                    error = $"byte格式错误: {text}";
                    return false;

                case "short":
                    if (TryParseInt16Flexible(text, out short shortValue))
                    {
                        typedValue = shortValue;
                        return true;
                    }
                    error = $"short格式错误: {text}";
                    return false;

                case "ushort":
                    if (TryParseUInt16Flexible(text, out ushort ushortValue))
                    {
                        typedValue = ushortValue;
                        return true;
                    }
                    error = $"ushort格式错误: {text}";
                    return false;

                case "int":
                    if (TryParseInt32Flexible(text, out int intValue))
                    {
                        typedValue = intValue;
                        return true;
                    }
                    error = $"int格式错误: {text}";
                    return false;

                case "uint":
                    if (TryParseUInt32Flexible(text, out uint uintValue))
                    {
                        typedValue = uintValue;
                        return true;
                    }
                    error = $"uint格式错误: {text}";
                    return false;

                case "long":
                    if (TryParseInt64Flexible(text, out long longValue))
                    {
                        typedValue = longValue;
                        return true;
                    }
                    error = $"long格式错误: {text}";
                    return false;

                case "ulong":
                    if (TryParseUInt64Flexible(text, out ulong ulongValue))
                    {
                        typedValue = ulongValue;
                        return true;
                    }
                    error = $"ulong格式错误: {text}";
                    return false;

                case "float":
                    if (TryParseSingleFlexible(text, out float singleValue))
                    {
                        typedValue = singleValue;
                        return true;
                    }
                    error = $"float格式错误: {text}";
                    return false;

                case "double":
                    if (TryParseDoubleFlexible(text, out double doubleValue))
                    {
                        typedValue = doubleValue;
                        return true;
                    }
                    error = $"double格式错误: {text}";
                    return false;

                case "string":
                    typedValue = rawValue ?? string.Empty;
                    return true;

                case "byte[]":
                    if (TryParseByteArray(text, out byte[] bytes, out string byteArrayErr))
                    {
                        typedValue = bytes;
                        return true;
                    }
                    error = byteArrayErr;
                    return false;

                default:
                    error = $"暂不支持的数据类型: {tagItem.DataTypeCode}";
                    return false;
            }
        }

        private DeviceResult ExecuteDeviceWrite(TagItem tagItem, object typedValue, string encodingStr)
        {
            string dataType = (tagItem.DataTypeCode ?? string.Empty).Trim().ToLowerInvariant();
            string address = tagItem.Address;

            switch (dataType)
            {
                case "bool":
                case "bit": return device.Write(address, (bool)typedValue);
                case "byte": return device.Write(address, new[] { (byte)typedValue });
                case "short": return device.Write(address, (short)typedValue);
                case "ushort": return device.Write(address, (ushort)typedValue);
                case "int": return device.Write(address, (int)typedValue);
                case "uint": return device.Write(address, (uint)typedValue);
                case "long": return device.Write(address, (long)typedValue);
                case "ulong": return device.Write(address, (ulong)typedValue);
                case "float": return device.Write(address, (float)typedValue);
                case "double": return device.Write(address, (double)typedValue);
                case "byte[]": return device.Write(address, (byte[])typedValue);
                case "string":
                    return WriteStringValue(tagItem, typedValue?.ToString() ?? string.Empty, encodingStr);
                default:
                    return DeviceResult.Fail("未支持的数据类型");
            }
        }

        private DeviceResult WriteStringValue(TagItem tagItem, string value, string encodingStr)
        {
            if (StringHasLeader && TryWriteStringWithLeader(tagItem, value, out DeviceResult leaderResult, out string leaderReason))
            {
                if (!string.IsNullOrWhiteSpace(leaderReason))
                {
                    OnLogs?.Invoke($"字符串写入(带前导)提示【{tagItem.Address}】：{leaderReason}");
                }
                return leaderResult;
            }

            if (string.Equals(encodingStr, "ASCII", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(encodingStr, "Default", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(encodingStr))
            {
                return device.Write(tagItem.Address, value);
            }

            try
            {
                return device.Write(tagItem.Address, value);
            }
            catch (Exception ex)
            {
                OnLogs?.Invoke($"字符串写入异常(重试): {ex.Message}");
                return device.Write(tagItem.Address, value);
            }
        }

        private bool TryWriteStringWithLeader(TagItem tagItem, string value, out DeviceResult writeResult, out string message)
        {
            writeResult = null;
            message = string.Empty;

            if (tagItem == null || string.IsNullOrWhiteSpace(tagItem.Address))
            {
                message = "地址为空，回退普通字符串写入";
                return false;
            }

            if (!TryAddOffsetToAddress(tagItem.Address, 2, out string contentAddress))
            {
                message = "地址格式无法偏移+2，回退普通字符串写入";
                return false;
            }

            Encoding encoding;
            try
            {
                encoding = string.IsNullOrEmpty(tagItem.StringEncoding) || tagItem.StringEncoding == "Default"
                    ? Encoding.Default : Encoding.GetEncoding(tagItem.StringEncoding);
            }
            catch
            {
                encoding = Encoding.ASCII;
            }

            byte[] rawBytes = encoding.GetBytes(value ?? string.Empty);
            int maxLen = tagItem.Length > 0 ? tagItem.Length : rawBytes.Length;
            maxLen = Math.Max(1, Math.Min(254, maxLen));
            int actualLen = Math.Min(rawBytes.Length, maxLen);

            byte[] content = new byte[maxLen];
            if (actualLen > 0)
            {
                Array.Copy(rawBytes, 0, content, 0, actualLen);
            }

            var leaderWrite = device.Write(tagItem.Address, new byte[] { (byte)maxLen, (byte)actualLen });
            if (!leaderWrite.IsSuccess)
            {
                writeResult = leaderWrite;
                message = $"写入长度前导失败: {leaderWrite.Message}";
                return true;
            }

            var contentWrite = device.Write(contentAddress, content);
            if (!contentWrite.IsSuccess)
            {
                message = $"写入字符串内容失败: {contentWrite.Message}";
            }

            writeResult = contentWrite;
            return true;
        }

        private static bool TryAddOffsetToAddress(string address, int byteOffset, out string result)
        {
            result = address;
            if (string.IsNullOrWhiteSpace(address)) return false;

            string[] parts = address.Split('.');
            if (parts.Length < 2) return false;

            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int currentOffset))
            {
                return false;
            }

            parts[1] = (currentOffset + byteOffset).ToString(CultureInfo.InvariantCulture);
            result = string.Join(".", parts);
            return true;
        }

        private static bool TryParseBoolFlexible(string text, out bool value)
        {
            value = false;
            if (bool.TryParse(text, out value)) return true;
            if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "on", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }
            if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "no", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }
            return false;
        }

        private static bool TryParseByteFlexible(string text, out byte value, bool allowBareHex)
        {
            value = 0;
            string t = (text ?? string.Empty).Trim();
            if (t.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
            {
                return byte.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }

            if (byte.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
            if (byte.TryParse(t, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)) return true;
            if (allowBareHex && byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return true;
            return false;
        }

        private static bool TryParseInt16Flexible(string text, out short value)
        {
            value = 0;
            string t = (text ?? string.Empty).Trim();
            if (t.StartsWith("0X", StringComparison.OrdinalIgnoreCase) &&
                ushort.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort hex))
            {
                value = unchecked((short)hex);
                return true;
            }
            if (short.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
            return short.TryParse(t, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
        }

        private static bool TryParseUInt16Flexible(string text, out ushort value)
        {
            value = 0;
            string t = (text ?? string.Empty).Trim();
            if (t.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
            {
                return ushort.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }
            if (ushort.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
            return ushort.TryParse(t, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
        }

        private static bool TryParseInt32Flexible(string text, out int value)
        {
            value = 0;
            string t = (text ?? string.Empty).Trim();
            if (t.StartsWith("0X", StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hex))
            {
                value = unchecked((int)hex);
                return true;
            }
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
            return int.TryParse(t, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
        }

        private static bool TryParseUInt32Flexible(string text, out uint value)
        {
            value = 0;
            string t = (text ?? string.Empty).Trim();
            if (t.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
            {
                return uint.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }
            if (uint.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
            return uint.TryParse(t, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
        }

        private static bool TryParseInt64Flexible(string text, out long value)
        {
            value = 0;
            string t = (text ?? string.Empty).Trim();
            if (t.StartsWith("0X", StringComparison.OrdinalIgnoreCase) &&
                ulong.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hex))
            {
                value = unchecked((long)hex);
                return true;
            }
            if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
            return long.TryParse(t, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
        }

        private static bool TryParseUInt64Flexible(string text, out ulong value)
        {
            value = 0;
            string t = (text ?? string.Empty).Trim();
            if (t.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
            {
                return ulong.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }
            if (ulong.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
            return ulong.TryParse(t, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
        }

        private static bool TryParseSingleFlexible(string text, out float value)
        {
            value = 0;
            if (float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value)) return true;
            return float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value);
        }

        private static bool TryParseDoubleFlexible(string text, out double value)
        {
            value = 0;
            if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value)) return true;
            return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value);
        }

        private static bool TryParseByteArray(string text, out byte[] value, out string error)
        {
            value = null;
            error = string.Empty;

            string normalized = (text ?? string.Empty)
                .Replace("[", " ")
                .Replace("]", " ")
                .Replace(",", " ")
                .Replace(";", " ")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            if (string.IsNullOrWhiteSpace(normalized))
            {
                error = "byte[] 不能为空";
                return false;
            }

            string[] tokens = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new List<byte>(tokens.Length);
            foreach (var token in tokens)
            {
                if (!TryParseByteFlexible(token, out byte b, true))
                {
                    error = $"byte[]元素解析失败: {token}";
                    return false;
                }
                bytes.Add(b);
            }

            value = bytes.ToArray();
            return true;
        }

        private static bool ShowInputDialog(string title, string prompt, string defaultValue, out string result)
        {
            result = defaultValue ?? string.Empty;
            using (var form = new Form())
            using (var label = new Label())
            using (var textBox = new TextBox())
            using (var buttonOk = new Button())
            using (var buttonCancel = new Button())
            {
                form.Text = title;
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(420, 130);

                label.AutoSize = true;
                label.Text = prompt;
                label.Left = 12;
                label.Top = 15;

                textBox.Left = 12;
                textBox.Top = 42;
                textBox.Width = 392;
                textBox.Text = defaultValue ?? string.Empty;

                buttonOk.Text = "确定";
                buttonOk.DialogResult = DialogResult.OK;
                buttonOk.Left = 248;
                buttonOk.Top = 82;
                buttonOk.Width = 75;

                buttonCancel.Text = "取消";
                buttonCancel.DialogResult = DialogResult.Cancel;
                buttonCancel.Left = 329;
                buttonCancel.Top = 82;
                buttonCancel.Width = 75;

                form.Controls.Add(label);
                form.Controls.Add(textBox);
                form.Controls.Add(buttonOk);
                form.Controls.Add(buttonCancel);
                form.AcceptButton = buttonOk;
                form.CancelButton = buttonCancel;

                if (form.ShowDialog() == DialogResult.OK)
                {
                    result = textBox.Text;
                    return true;
                }

                return false;
            }
        }

        private void DeleteRowsSafely(IReadOnlyList<DataGridViewRow> rows)
        {
            if (rows == null || rows.Count == 0) return;

            var indices = rows
                .Where(r => r != null && !r.IsNewRow && r.Index >= 0)
                .Select(r => r.Index)
                .Distinct()
                .OrderByDescending(i => i)
                .ToList();

            if (indices.Count == 0) return;

            dgv_Tags.SuspendLayout();
            try
            {
                foreach (var index in indices)
                {
                    if (index < 0 || index >= dgv_Tags.Rows.Count) continue;
                    if (dgv_Tags.Rows[index].IsNewRow) continue;
                    dgv_Tags.Rows.RemoveAt(index);
                }
            }
            finally
            {
                dgv_Tags.ResumeLayout();
            }
        }

        #region DGV UI细节事件

        /// <summary>
        /// 行号绘制——在 DataGridView 行标题区域显示行号数字。
        /// 
        /// 优化说明：此方法用 TextRenderer.DrawText 替代了 WinForms 默认的自绘制方案。
        /// 之前的实现同时订阅 RowPrePaint 和 RowPostPaint，并在每个事件中调用
        /// MeasureText 计算文字位置，导致频繁滚动时大量 GDI 调用，CPU 爆高。
        /// 
        /// 当前方案：
        /// - 只保留 RowPostPaint（取消 RowPrePaint），避免重复绘制
        /// - 用 TextRenderer.DrawText 替代 DrawString + MeasureText（TextRenderer 内部优化了 GDI 调用）
        /// - 行号字符串只做一次 ToString，不重复实例化
        /// </summary>
        private void dgv_Tags_RowPostPaint(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            var idx = (e.RowIndex + 1).ToString(CultureInfo.InvariantCulture);
            var headerBounds = e.RowBounds;
            headerBounds.Offset(0, 0);
            headerBounds.Width = dgv_Tags.RowHeadersWidth;
            TextRenderer.DrawText(e.Graphics, idx,
                dgv_Tags.RowHeadersDefaultCellStyle.Font,
                headerBounds,
                dgv_Tags.RowHeadersDefaultCellStyle.ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void dgv_Tags_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (dgv_Tags.Columns == null || dgv_Tags.Columns.Count == 0) return;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            if (e.ColumnIndex == dgv_Tags.Columns[0].Index)
            {
                var row = dgv_Tags.Rows[e.RowIndex];
                object rawValue = row.Cells[0].Value;
                bool isChecked = false;
                if (rawValue is bool boolValue)
                {
                    isChecked = boolValue;
                }
                else if (rawValue != null && rawValue != DBNull.Value)
                {
                    bool.TryParse(rawValue.ToString(), out isChecked);
                }

                if (!isChecked)
                    row.Cells[TagValCellIndex].Value = "";

                if (!(rawValue is bool current && current == isChecked))
                    row.Cells[0].Value = isChecked;
            }

            if (e.ColumnIndex != TagValCellIndex)
                UpdateTagItemList();
        }

        private void dgv_Tags_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (dgv_Tags.IsCurrentCellDirty)
            {
                dgv_Tags.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void TagListUpdateDebounceTimer_Tick(object sender, EventArgs e)
        {
            tagListUpdateDebounceTimer.Stop();
            if (!tagListUpdatePending) return;
            tagListUpdatePending = false;
            UpdateTagItemListCore();
        }

        private void RequestUpdateTagItemList(bool immediate)
        {
            if (immediate)
            {
                tagListUpdatePending = false;
                tagListUpdateDebounceTimer.Stop();
                UpdateTagItemListCore();
                return;
            }

            tagListUpdatePending = true;
            tagListUpdateDebounceTimer.Stop();
            tagListUpdateDebounceTimer.Start();
        }

        private void UpdateTagItemList()
        {
            RequestUpdateTagItemList(false);
        }

        private void UpdateTagItemListCore()
        {
            if (IsDisposed || dgv_Tags.IsDisposed) return;

            tagDgvRowDic.Clear();

            int firstRow = 0;
            int rowCount = dgv_Tags.RowCount;

            if (dgv_Tags.Rows.Count > 0) setRefreshEnable(true);
            for (int i = 0; i < rowCount; i++)
            {
                if (i + firstRow >= dgv_Tags.Rows.Count) break;

                var row = dgv_Tags.Rows[i + firstRow];
                if (!row.IsNewRow)
                {
                    TagItem tagItem = GetTagItem(row);
                    if (tagItem.Enable)
                        tagDgvRowDic.TryAdd(row, tagItem);
                }
            }

            setRefreshEnable(tagDgvRowDic.Count > 0);
            _batchMonitor?.BuildPlan(tagDgvRowDic.Values.Where(t => t != null && t.Enable));
        }
        #endregion
    }
}