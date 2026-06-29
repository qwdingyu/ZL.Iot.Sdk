using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ZL.Iot.Controls.Controls.Curve
{
    /// <summary>合并策略：当两点位置过近时如何处理</summary>
    public enum MergeMode
    {
        MergeAverage,   // 新值与最后值平均（适合噪声较大的信号）
        OverwriteLast,  // 新值覆盖最后一点（适合快速刷新保持锐度）
        ForceNew        // 强制新点（x 微小递增，不丢任何数据）
    }

    /// <summary>
    /// 曲线基类：管理 OxyPlot 系列、轴、裁剪、滑动平均、合并、刷新节流、线程安全。
    /// 
    /// 职责：
    /// 1. 持有 OxyPlot 的 PlotModel + PlotView 引用
    /// 2. 管理多条曲线（LineSeries），支持按名称增删查
    /// 3. 自动 X 轴裁剪——仅保留 VisibleWindowSeconds 内的数据点
    /// 4. 滑动平均滤波（MovingAverageWindow）
    /// 5. 两点合并策略（MergeMode）——高频采集时避免点数爆炸
    /// 6. 刷新节流（RefreshThrottleMs）——控制 OxyPlot 重绘频率
    /// 7. 线程安全：AddPointThreadSafe 通过 BeginInvoke 跨线程投递
    /// 
    /// 派生类：
    /// - TimeCurve：X 轴为 DateTime，适用于实时趋势图
    /// </summary>
    public abstract class CurveBase : IDisposable
    {
        protected readonly PlotView plotView;
        protected readonly PlotModel plotModel;
        protected readonly Axis xAxis;
        protected readonly LinearAxis yAxis;

        public LinearAxis YAxis => yAxis;
        public string Title
        {
            get => plotModel.Title;
            set { plotModel.Title = value; plotModel.InvalidatePlot(false); }
        }

        protected Stopwatch refreshWatch = Stopwatch.StartNew();

        public int RefreshThrottleMs { get; set; } = 120;
        public int SeriesMaxPoints { get; set; } = 3000;
        public int MinPointIntervalMs { get; set; } = 5;
        public MergeMode MergeMode { get; set; } = MergeMode.MergeAverage;
        public double VisibleWindowSeconds { get; set; } = 60;

        protected class SeriesInfo
        {
            public string Name;
            public LineSeries Series;
            public Queue<double> AvgQueue;
            public double AvgSum;
            public int MovingAverageWindow = 1;
            public double LastX = double.NaN;
            public DateTime? LastDateTime = null;
            public double NextAutoIndex = 0;
        }

        protected readonly Dictionary<string, SeriesInfo> seriesMap = new(StringComparer.OrdinalIgnoreCase);

        protected CurveBase(PlotView plotView, Axis xAxis, string title = null)
        {
            this.plotView = plotView ?? throw new ArgumentNullException(nameof(plotView));
            this.xAxis = xAxis ?? throw new ArgumentNullException(nameof(xAxis));
            plotModel = new PlotModel { Title = title };
            yAxis = new LinearAxis
            {
                Position = AxisPosition.Left, Title = "Value",
                Minimum = double.NaN, Maximum = double.NaN,
                IsZoomEnabled = false, IsPanEnabled = false
            };
            plotModel.Axes.Add(this.xAxis);
            plotModel.Axes.Add(yAxis);
            plotView.Model = plotModel;
        }

        public LineSeries AddSeries(string name, OxyColor? color = null, int movingAverageWindow = 1)
        {
            if (string.IsNullOrWhiteSpace(name)) name = Guid.NewGuid().ToString();
            if (seriesMap.TryGetValue(name, out var exist)) return exist.Series;

            var ls = new LineSeries
            {
                Title = name, Color = color ?? OxyColors.Automatic,
                StrokeThickness = 1.5, MarkerType = MarkerType.None, LineJoin = LineJoin.Round
            };
            plotModel.Series.Add(ls);
            seriesMap[name] = new SeriesInfo
            {
                Name = name, Series = ls,
                AvgQueue = movingAverageWindow > 1 ? new Queue<double>() : null,
                MovingAverageWindow = Math.Max(1, movingAverageWindow),
                LastX = double.NaN
            };
            plotModel.InvalidatePlot(false);
            return ls;
        }

        public bool RemoveSeries(string name)
        {
            if (!seriesMap.TryGetValue(name, out var info)) return false;
            plotModel.Series.Remove(info.Series);
            seriesMap.Remove(name);
            plotModel.InvalidatePlot(false);
            return true;
        }

        public void ShowSeries(string name, bool visible)
        {
            if (seriesMap.TryGetValue(name, out var info))
            { info.Series.IsVisible = visible; plotModel.InvalidatePlot(false); }
        }

        /// <summary>
        /// 线程安全的数据点添加入口。
        /// 
        /// 如果当前在非 UI 线程（InvokeRequired==true），则通过 BeginInvoke 投递到 UI 线程执行。
        /// 如果在 UI 线程上（如来自 BeginInvoke 闭包本身），直接调用 AddPointInternal。
        /// 
        /// 使用 BeginInvoke 而非 Invoke 的原因：
        /// - BeginInvoke 是异步投递，调用线程不阻塞
        /// - 趋势图数据来自后台采集线程（RefreshDataAsync），不应被 UI 渲染阻塞
        /// - catch { } 静默丢弃异常：当控件已 Dispose 但后台线程仍在投递时，BeginInvoke 可能失败
        /// </summary>
        protected void AddPointThreadSafe(string seriesName, double x, double y, DateTime? timestamp = null)
        {
            if (plotView.InvokeRequired)
            {
                try { plotView.BeginInvoke(new Action(() => AddPointInternal(seriesName, x, y, timestamp))); }
                catch { /* 控件已 Dispose 时静默忽略 */ }
            }
            else AddPointInternal(seriesName, x, y, timestamp);
        }

        /// <summary>
        /// 内部数据点添加逻辑（必须在 UI 线程上执行）。
        /// 
        /// 流程：
        /// 1. 查找或创建 SeriesInfo（曲线元数据）
        /// 2. 滑动平均滤波（如果 MovingAverageWindow > 1）
        /// 3. X 轴极值检查 + 合并策略
        /// 4. 添加数据点到 LineSeries
        /// 5. X 轴自动裁剪——移除 VisibleWindowSeconds 之外的点
        /// 6. 点数上限检查——超过 SeriesMaxPoints 时裁剪
        /// 7. 刷新节流——RefreshThrottleMs 内不重复 InvalidatePlot
        /// </summary>
        private void AddPointInternal(string seriesName, double x, double y, DateTime? timestamp = null)
        {
            if (!seriesMap.TryGetValue(seriesName, out var info))
            { AddSeries(seriesName, null, 1); info = seriesMap[seriesName]; }

            double yUsed = y;
            if (info.MovingAverageWindow > 1 && info.AvgQueue != null)
            {
                info.AvgQueue.Enqueue(y);
                info.AvgSum += y;
                if (info.AvgQueue.Count > info.MovingAverageWindow)
                    info.AvgSum -= info.AvgQueue.Dequeue();
                yUsed = info.AvgSum / info.AvgQueue.Count;
            }

            double minXInterval = GetMinXIntervalInXUnits();
            if (!double.IsNaN(info.LastX) && x <= info.LastX + minXInterval)
            {
                switch (MergeMode)
                {
                    case MergeMode.MergeAverage:
                        if (info.Series.Points.Count > 0)
                        {
                            var last = info.Series.Points[info.Series.Points.Count - 1];
                            info.Series.Points[info.Series.Points.Count - 1] = new DataPoint(last.X, (last.Y + yUsed) / 2.0);
                        }
                        break;
                    case MergeMode.OverwriteLast:
                        if (info.Series.Points.Count > 0)
                        {
                            var last = info.Series.Points[info.Series.Points.Count - 1];
                            info.Series.Points[info.Series.Points.Count - 1] = new DataPoint(last.X, yUsed);
                        }
                        break;
                    case MergeMode.ForceNew:
                        x = info.LastX + Math.Max(minXInterval, GetMinimalXIncrement());
                        info.Series.Points.Add(new DataPoint(x, yUsed));
                        info.LastX = x; info.LastDateTime = timestamp;
                        break;
                }
                if (timestamp.HasValue) info.LastDateTime = timestamp;
                TryRefresh();
                return;
            }

            if (!double.IsNaN(info.LastX) && x <= info.LastX)
                x = info.LastX + GetMinimalXIncrement();

            info.Series.Points.Add(new DataPoint(x, yUsed));
            info.LastX = x;
            if (timestamp.HasValue) info.LastDateTime = timestamp;

            if (info.Series.Points.Count > SeriesMaxPoints)
                info.Series.Points.RemoveRange(0, info.Series.Points.Count - SeriesMaxPoints);

            UpdateXAxisWindow(x);
            TryRefresh();
        }

        protected virtual double GetMinXIntervalInXUnits() => double.Epsilon;
        protected virtual double GetMinimalXIncrement() => 1e-9;
        protected abstract void UpdateXAxisWindow(double latestX);

        private void TryRefresh()
        {
            if (refreshWatch.ElapsedMilliseconds >= RefreshThrottleMs)
            { plotModel.InvalidatePlot(true); refreshWatch.Restart(); }
        }

        public virtual void ClearAll()
        {
            foreach (var kv in seriesMap)
            {
                kv.Value.Series.Points.Clear();
                kv.Value.AvgQueue?.Clear();
                kv.Value.AvgSum = 0;
                kv.Value.LastX = double.NaN;
                kv.Value.NextAutoIndex = 0;
            }
            plotModel.InvalidatePlot(true);
        }

        public virtual void SetYLimits(double min, double max)
        { yAxis.Minimum = min; yAxis.Maximum = max; plotModel.InvalidatePlot(false); }

        public virtual void EnableYAxisAutoScale()
        { yAxis.Minimum = double.NaN; yAxis.Maximum = double.NaN; plotModel.InvalidatePlot(false); }

        public virtual void SetYAxisTitle(string title)
        { yAxis.Title = title; plotModel.InvalidatePlot(false); }

        public virtual void Dispose()
        {
            // PlotModel/PlotView/LineSeries 由 WinForms 父控件自动释放，
            // OxyPlot 的 PlotModel 不实现 IDisposable，无需显式清理。
            // SeriesMap 和裁剪定时器在父控件 Dispose 时一并回收。
        }
    }

    /// <summary>时间轴曲线：X 轴为 DateTimeAxis，支持滑动窗口</summary>
    public class TimeCurve : CurveBase
    {
        public new int MinPointIntervalMs
        {
            get => base.MinPointIntervalMs;
            set => base.MinPointIntervalMs = value;
        }

        protected override double GetMinXIntervalInXUnits()
            => TimeSpan.FromMilliseconds(MinPointIntervalMs).TotalDays;
        protected override double GetMinimalXIncrement()
            => TimeSpan.FromMilliseconds(1).TotalDays;

        public TimeCurve(PlotView plotView, string title) : base(plotView,
            new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "mm:ss",
                MinorIntervalType = DateTimeIntervalType.Seconds,
                IsZoomEnabled = false, IsPanEnabled = false, Title = "时间"
            }, title)
        { yAxis.Title = "值"; }

        public void AddPoint(string seriesName, DateTime timestamp, double value)
            => AddPointThreadSafe(seriesName, DateTimeAxis.ToDouble(timestamp), value, timestamp);

        protected override void UpdateXAxisWindow(double latestX)
        {
            double earliestX = double.PositiveInfinity;
            bool hasPoint = false;
            foreach (var kv in seriesMap)
            {
                var pts = kv.Value.Series.Points;
                if (pts.Count > 0) { hasPoint = true; if (pts[0].X < earliestX) earliestX = pts[0].X; }
            }
            if (!hasPoint)
            {
                var latest = DateTimeAxis.ToDateTime(latestX);
                xAxis.Minimum = DateTimeAxis.ToDouble(latest.AddSeconds(-VisibleWindowSeconds));
                xAxis.Maximum = latestX;
                return;
            }
            double span = (DateTimeAxis.ToDateTime(latestX) - DateTimeAxis.ToDateTime(earliestX)).TotalSeconds;
            if (span <= VisibleWindowSeconds)
            { xAxis.Minimum = earliestX; xAxis.Maximum = latestX; }
            else
            {
                xAxis.Minimum = DateTimeAxis.ToDouble(
                    DateTimeAxis.ToDateTime(latestX).AddSeconds(-VisibleWindowSeconds));
                xAxis.Maximum = latestX;
            }
        }
    }

    /// <summary>索引轴曲线：X 从 0 自增，适合等间隔采样</summary>
    public class IndexCurve : CurveBase
    {
        public double UnitPerSample { get; set; } = 1.0;

        public IndexCurve(PlotView plotView, string title, string xTitle = "Index") : base(plotView,
            new LinearAxis
            {
                Position = AxisPosition.Bottom,
                IsZoomEnabled = false, IsPanEnabled = false, Title = xTitle
            }, title)
        { yAxis.Title = "值"; }

        protected override double GetMinXIntervalInXUnits() => 1e-9;
        protected override double GetMinimalXIncrement() => UnitPerSample * 1e-6;

        public void AddPoint(string seriesName, double value)
        {
            if (!seriesMap.TryGetValue(seriesName, out var info))
            { AddSeries(seriesName, null, 1); info = seriesMap[seriesName]; }
            double x = info.NextAutoIndex * UnitPerSample;
            info.NextAutoIndex += 1.0;
            AddPointThreadSafe(seriesName, x, value, null);
        }

        protected override void UpdateXAxisWindow(double latestX)
        {
            double min = latestX - VisibleWindowSeconds;
            if (min < 0) min = 0;
            xAxis.Minimum = min;
            xAxis.Maximum = latestX;
        }
    }
}