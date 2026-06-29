using OxyPlot.WindowsForms;

namespace ZL.Iot.Controls.Controls.Curve
{
    partial class UcTrendPanel
    {
        private System.ComponentModel.IContainer components = null;

        private PlotView plotView;
        private System.Windows.Forms.FlowLayoutPanel toolStrip;
        private System.Windows.Forms.CheckBox chkPause;
        private System.Windows.Forms.Button btnClear;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;

            this.toolStrip = new System.Windows.Forms.FlowLayoutPanel();
            this.toolStrip.Dock = System.Windows.Forms.DockStyle.Top;
            this.toolStrip.Height = 30;
            this.toolStrip.BackColor = System.Drawing.Color.FromArgb(245, 245, 245);
            this.toolStrip.Padding = new System.Windows.Forms.Padding(6, 3, 6, 3);

            this.chkPause = new System.Windows.Forms.CheckBox();
            this.chkPause.Text = "暂停";
            this.chkPause.AutoSize = true;
            this.chkPause.Margin = new System.Windows.Forms.Padding(0, 4, 10, 0);

            this.btnClear = new System.Windows.Forms.Button();
            this.btnClear.Text = "清空";
            this.btnClear.AutoSize = true;
            this.btnClear.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnClear.UseVisualStyleBackColor = false;

            this.toolStrip.Controls.Add(this.chkPause);
            this.toolStrip.Controls.Add(this.btnClear);

            this.plotView = new PlotView();
            this.plotView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.plotView.BackColor = System.Drawing.Color.White;

            this.Controls.Add(this.plotView);
            this.Controls.Add(this.toolStrip);
        }

        #endregion
    }
}