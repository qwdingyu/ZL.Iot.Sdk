using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using ZL.Iot.Controls.Theme;

namespace ZL.Iot.Controls.Controls
{
    /// <summary>
    /// 日志面板控件 —— 显示采集/连接/错误日志。
    /// 
    /// 特性：
    /// - 自动颜色编码：SUCCESS=绿色、ERROR=红色、WARN=橙色、RAW=蓝色、其他=灰色
    /// - HEX 报文显示：16 字节一行，右侧 ASCII 对照
    /// - 暂停滚动：大量日志时暂停自动滚动到底部
    /// - 显示HEX 切换：控制 RAW 报文是否可见
    /// 
    /// 性能注意：
    /// - Append() 是同步调用，频繁写入（如每秒数百条日志）会卡 UI
    /// - 当前采集循环中 SetLogs 使用防重复过滤，仅值变化时才输出日志
    /// - 大量标签写入时，日志输出频率不应高于值变化频率
    /// 
    /// 线程安全：
    /// - Append() 必须在 UI 线程调用（从后台线程发起的日志应通过 BeginInvoke 投递）
    /// - 当前调用路径：采集线程 → BeginInvoke → UI 更新 → OnLogs → Append
    /// </summary>
    public partial class UcConsolePanel : UserControl
    {
        private bool _pauseScroll;
        private bool _hexMode;

        public UcConsolePanel()
        {
            InitializeComponent();
            SetStyle();
        }

        private void SetStyle()
        {
            BackColor = Theme.AppTheme.BgPanel;
            rtbLog.BackColor = Color.White;
            rtbLog.ForeColor = Theme.AppTheme.Text;
            rtbLog.Font = new Font("Consolas", 9.5f);
        }

        /// <summary>追加日志（自动颜色编码）</summary>
        public void Append(string level, string message)
        {
            if (rtbLog.IsDisposed || _pauseScroll) return;
            rtbLog.SuspendLayout();
            try
            {
                var time = DateTime.Now.ToString("HH:mm:ss");
                AppendColored($"[{time}] ", Theme.AppTheme.TextSec);
                var lv = level.ToUpperInvariant();
                var lc = lv switch
                {
                    "SUCCESS" => Theme.AppTheme.LogSuccess,
                    "ERROR" => Theme.AppTheme.LogError,
                    "WARN" => Theme.AppTheme.LogWarn,
                    "RAW" => Theme.AppTheme.LogRaw,
                    _ => Theme.AppTheme.LogInfo,
                };
                AppendColored($"[{lv}] ", lc);
                AppendColored(message + Environment.NewLine, Theme.AppTheme.Text);
            }
            finally { rtbLog.ResumeLayout(); ScrollToEnd(); }
        }

        /// <summary>追加 HEX 报文（十六进制 + ASCII 对照）</summary>
        public void AppendHex(string description, byte[] data)
        {
            if (rtbLog.IsDisposed || !_hexMode || data == null || data.Length == 0) return;
            rtbLog.SuspendLayout();
            try
            {
                var time = DateTime.Now.ToString("HH:mm:ss");
                AppendColored($"[{time}] ", Theme.AppTheme.TextSec);
                AppendColored("[RAW] ", Theme.AppTheme.LogRaw);
                AppendColored($"{description} ({data.Length}B)\n", Theme.AppTheme.Text);

                for (int i = 0; i < data.Length; i += 16)
                {
                    var hex = new StringBuilder("      ");
                    var asc = new StringBuilder("  ");
                    for (int j = 0; j < 16 && i + j < data.Length; j++)
                    {
                        hex.Append($"{data[i + j]:X2} ");
                        asc.Append(data[i + j] >= 0x20 && data[i + j] < 0x7F ? (char)data[i + j] : '.');
                    }
                    int rem = 16 - Math.Min(16, data.Length - i);
                    hex.Append(' ', rem * 3);
                    AppendColored(hex.ToString(), Theme.AppTheme.LogRaw);
                    AppendColored(asc + "\n", Theme.AppTheme.TextSec);
                }
            }
            finally { rtbLog.ResumeLayout(); ScrollToEnd(); }
        }

        private void AppendColored(string text, Color color)
        {
            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.SelectionLength = 0;
            rtbLog.SelectionColor = color;
            rtbLog.AppendText(text);
        }

        private void ScrollToEnd()
        {
            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.ScrollToCaret();
        }

        public void ClearLog() => rtbLog.Clear();
        private void btnClear_Click(object sender, EventArgs e) => ClearLog();

        private void chkPause_CheckedChanged(object sender, EventArgs e) =>
            _pauseScroll = chkPause.Checked;

        private void chkHex_CheckedChanged(object sender, EventArgs e) =>
            _hexMode = chkHex.Checked;
    }
}