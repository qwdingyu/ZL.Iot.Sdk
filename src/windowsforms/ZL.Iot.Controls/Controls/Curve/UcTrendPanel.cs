using System;
using System.Collections.Generic;
using System.Windows.Forms;
using OxyPlot;

namespace ZL.Iot.Controls.Controls.Curve
{
    /// <summary>
    /// 趋势图面板 —— 独立的 WinForms 控件，可嵌入任何 Tab。
    /// 使用 TimeCurve 实时显示标签值变化曲线。
    /// </summary>
    public partial class UcTrendPanel : UserControl
    {
        private TimeCurve timeCurve;
        private bool _paused;
        private readonly Dictionary<string, OxyColor> _seriesColors = new();
        private static readonly OxyColor[] _palette = new[]
        {
            OxyColors.DodgerBlue, OxyColors.OrangeRed,
            OxyColors.LimeGreen, OxyColors.Gold,
            OxyColors.MediumPurple, OxyColors.DeepPink,
            OxyColors.Cyan, OxyColors.SaddleBrown,
        };
        private int _colorIndex;

        public UcTrendPanel()
        {
            InitializeComponent();
            SetupEvents();
            InitPlot();
        }

        private void SetupEvents()
        {
            chkPause.CheckedChanged += (_, _) => _paused = chkPause.Checked;
            btnClear.Click += (_, _) => timeCurve?.ClearAll();
        }

        private void InitPlot()
        {
            timeCurve = new TimeCurve(plotView, "实时趋势");
        }

        /// <summary>添加一个数据点到趋势图</summary>
        /// <param name="seriesName">曲线名称（通常 = 标签名或地址）</param>
        /// <param name="value">数值</param>
        /// <param name="timestamp">时间戳，null 则用当前时间</param>
        public void AddValue(string seriesName, double value, DateTime? timestamp = null)
        {
            if (_paused || timeCurve == null) return;
            var t = timestamp ?? DateTime.Now;
            timeCurve.AddPoint(seriesName, t, value);
        }

        /// <summary>添加一个新的曲线系列（指定颜色，否则自动分配）</summary>
        public void EnsureSeries(string name, OxyColor? color = null)
        {
            color ??= _palette[_colorIndex++ % _palette.Length];
            timeCurve?.AddSeries(name, color.Value);
        }

        /// <summary>清空所有曲线</summary>
        public void ClearAll() => timeCurve?.ClearAll();

        /// <summary>设置 Y 轴范围</summary>
        public void SetYRange(double min, double max) => timeCurve?.SetYLimits(min, max);

        /// <summary>启用 Y 轴自动缩放</summary>
        public void EnableYAutoScale() => timeCurve?.EnableYAxisAutoScale();

        /// <summary>设置显示时间窗口（秒）</summary>
        public void SetWindowSeconds(double seconds)
        {
            if (timeCurve != null) timeCurve.VisibleWindowSeconds = seconds;
        }

        /// <summary>暂停/继续绘图</summary>
        public bool Paused { get => _paused; set { _paused = value; chkPause.Checked = value; } }
    }
}