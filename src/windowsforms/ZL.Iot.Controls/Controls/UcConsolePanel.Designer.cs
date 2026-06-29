namespace ZL.Iot.Controls.Controls
{
    partial class UcConsolePanel
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.RichTextBox rtbLog;
        private System.Windows.Forms.Button btnClear;
        private System.Windows.Forms.CheckBox chkPause;
        private System.Windows.Forms.CheckBox chkHex;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码
        private void InitializeComponent()
        {
            this.rtbLog = new System.Windows.Forms.RichTextBox();
            this.btnClear = new System.Windows.Forms.Button();
            this.chkPause = new System.Windows.Forms.CheckBox();
            this.chkHex = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();

            // rtbLog
            this.rtbLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rtbLog.Location = new System.Drawing.Point(0, 0);
            this.rtbLog.Name = "rtbLog";
            this.rtbLog.ReadOnly = true;
            this.rtbLog.Size = new System.Drawing.Size(600, 150);
            this.rtbLog.TabIndex = 0;
            this.rtbLog.Text = "";
            this.rtbLog.WordWrap = false;

            // btnClear
            this.btnClear.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            this.btnClear.Location = new System.Drawing.Point(520, 120);
            this.btnClear.Name = "btnClear";
            this.btnClear.Size = new System.Drawing.Size(70, 25);
            this.btnClear.TabIndex = 1;
            this.btnClear.Text = "清空";
            this.btnClear.UseVisualStyleBackColor = false;
            this.btnClear.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnClear.Click += new System.EventHandler(this.btnClear_Click);

            // chkPause
            this.chkPause.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            this.chkPause.AutoSize = true;
            this.chkPause.Location = new System.Drawing.Point(430, 124);
            this.chkPause.Name = "chkPause";
            this.chkPause.Size = new System.Drawing.Size(84, 16);
            this.chkPause.TabIndex = 2;
            this.chkPause.Text = "暂停滚动";
            this.chkPause.UseVisualStyleBackColor = true;
            this.chkPause.CheckedChanged += new System.EventHandler(this.chkPause_CheckedChanged);

            // chkHex
            this.chkHex.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            this.chkHex.AutoSize = true;
            this.chkHex.Location = new System.Drawing.Point(340, 124);
            this.chkHex.Name = "chkHex";
            this.chkHex.Size = new System.Drawing.Size(84, 16);
            this.chkHex.TabIndex = 3;
            this.chkHex.Text = "显示HEX";
            this.chkHex.UseVisualStyleBackColor = true;
            this.chkHex.CheckedChanged += new System.EventHandler(this.chkHex_CheckedChanged);

            // UcConsolePanel
            this.Controls.Add(this.rtbLog);
            this.Controls.Add(this.btnClear);
            this.Controls.Add(this.chkPause);
            this.Controls.Add(this.chkHex);
            this.Name = "UcConsolePanel";
            this.Size = new System.Drawing.Size(600, 150);
            this.ResumeLayout(false);
            this.PerformLayout();
            #endregion
        }
    }
}