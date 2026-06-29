namespace ZL.Iot.Controls.Controls
{
    partial class UcConnectionPanel
    {
        private System.ComponentModel.IContainer components = null;

        // 连接控件
        private System.Windows.Forms.Label lblIp;
        private System.Windows.Forms.TextBox txtIp;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.TextBox txtPort;
        private System.Windows.Forms.Button btnConnect;
        private System.Windows.Forms.Panel lampStatus;   // 12x12 状态指示灯

        // 扩展控件
        private System.Windows.Forms.Label lblProtocol;
        private System.Windows.Forms.ComboBox cmbProtocol;
        private System.Windows.Forms.Label lblRack;
        private System.Windows.Forms.NumericUpDown nudRack;
        private System.Windows.Forms.Label lblSlot;
        private System.Windows.Forms.NumericUpDown nudSlot;
        private System.Windows.Forms.CheckBox chkUseHsl;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        private void InitializeComponent()
        {
            this.lblProtocol = new System.Windows.Forms.Label();
            this.cmbProtocol = new System.Windows.Forms.ComboBox();
            this.lblIp = new System.Windows.Forms.Label();
            this.txtIp = new System.Windows.Forms.TextBox();
            this.lblPort = new System.Windows.Forms.Label();
            this.txtPort = new System.Windows.Forms.TextBox();
            this.lblRack = new System.Windows.Forms.Label();
            this.nudRack = new System.Windows.Forms.NumericUpDown();
            this.lblSlot = new System.Windows.Forms.Label();
            this.nudSlot = new System.Windows.Forms.NumericUpDown();
            this.btnConnect = new System.Windows.Forms.Button();
            this.chkUseHsl = new System.Windows.Forms.CheckBox();
            this.lampStatus = new System.Windows.Forms.Panel();
            ((System.ComponentModel.ISupportInitialize)(this.nudRack)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudSlot)).BeginInit();
            this.SuspendLayout();
            // 
            // lblProtocol
            // 
            this.lblProtocol.AutoSize = true;
            this.lblProtocol.Location = new System.Drawing.Point(4, 12);
            this.lblProtocol.Name = "lblProtocol";
            this.lblProtocol.Size = new System.Drawing.Size(32, 17);
            this.lblProtocol.Text = "协议:";
            this.lblProtocol.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // cmbProtocol
            // 
            this.cmbProtocol.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbProtocol.FormattingEnabled = true;
            this.cmbProtocol.Location = new System.Drawing.Point(42, 8);
            this.cmbProtocol.Name = "cmbProtocol";
            this.cmbProtocol.Size = new System.Drawing.Size(120, 25);
            this.cmbProtocol.SelectedIndexChanged += new System.EventHandler(this.cmbProtocol_SelectedIndexChanged);
            // 
            // lblIp
            // 
            this.lblIp.AutoSize = true;
            this.lblIp.Location = new System.Drawing.Point(170, 12);
            this.lblIp.Name = "lblIp";
            this.lblIp.Size = new System.Drawing.Size(24, 17);
            this.lblIp.Text = "IP:";
            this.lblIp.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // txtIp
            // 
            this.txtIp.Location = new System.Drawing.Point(196, 8);
            this.txtIp.Name = "txtIp";
            this.txtIp.Size = new System.Drawing.Size(130, 23);
            this.txtIp.Text = "127.0.0.1";
            // 
            // lblPort
            // 
            this.lblPort.AutoSize = true;
            this.lblPort.Location = new System.Drawing.Point(334, 12);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new System.Drawing.Size(32, 17);
            this.lblPort.Text = "端口:";
            this.lblPort.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // txtPort
            // 
            this.txtPort.Location = new System.Drawing.Point(368, 8);
            this.txtPort.Name = "txtPort";
            this.txtPort.Size = new System.Drawing.Size(52, 23);
            this.txtPort.Text = "102";
            // 
            // lblRack
            // 
            this.lblRack.AutoSize = true;
            this.lblRack.Location = new System.Drawing.Point(430, 12);
            this.lblRack.Name = "lblRack";
            this.lblRack.Size = new System.Drawing.Size(32, 17);
            this.lblRack.Text = "机架:";
            this.lblRack.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // nudRack
            // 
            this.nudRack.Location = new System.Drawing.Point(468, 8);
            this.nudRack.Maximum = new decimal(new int[] { 7, 0, 0, 0 });
            this.nudRack.Name = "nudRack";
            this.nudRack.Size = new System.Drawing.Size(44, 23);
            this.nudRack.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // lblSlot
            // 
            this.lblSlot.AutoSize = true;
            this.lblSlot.Location = new System.Drawing.Point(520, 12);
            this.lblSlot.Name = "lblSlot";
            this.lblSlot.Size = new System.Drawing.Size(32, 17);
            this.lblSlot.Text = "槽位:";
            this.lblSlot.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // nudSlot
            // 
            this.nudSlot.Location = new System.Drawing.Point(558, 8);
            this.nudSlot.Maximum = new decimal(new int[] { 7, 0, 0, 0 });
            this.nudSlot.Name = "nudSlot";
            this.nudSlot.Size = new System.Drawing.Size(44, 23);
            this.nudSlot.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // btnConnect
            // 
            this.btnConnect.Location = new System.Drawing.Point(610, 6);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(80, 28);
            this.btnConnect.Text = "连接";
            this.btnConnect.UseVisualStyleBackColor = false;
            this.btnConnect.Click += new System.EventHandler(this.btnConnect_Click);
            // 
            // chkUseHsl
            // 
            this.chkUseHsl.AutoSize = true;
            this.chkUseHsl.Location = new System.Drawing.Point(700, 10);
            this.chkUseHsl.Name = "chkUseHsl";
            this.chkUseHsl.Size = new System.Drawing.Size(130, 21);
            this.chkUseHsl.Text = "使用 HSL 驱动后端";
            this.chkUseHsl.UseVisualStyleBackColor = true;
            // 
            // lampStatus
            // 
            this.lampStatus.Location = new System.Drawing.Point(840, 12);
            this.lampStatus.Name = "lampStatus";
            this.lampStatus.Size = new System.Drawing.Size(14, 14);
            this.lampStatus.BackColor = System.Drawing.Color.FromArgb(0xCC, 0x00, 0x00); // 默认红色
            this.lampStatus.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            // 
            // UcConnectionPanel
            // 
            this.Controls.Add(this.lblProtocol);
            this.Controls.Add(this.cmbProtocol);
            this.Controls.Add(this.lblIp);
            this.Controls.Add(this.txtIp);
            this.Controls.Add(this.lblPort);
            this.Controls.Add(this.txtPort);
            this.Controls.Add(this.lblRack);
            this.Controls.Add(this.nudRack);
            this.Controls.Add(this.lblSlot);
            this.Controls.Add(this.nudSlot);
            this.Controls.Add(this.btnConnect);
            this.Controls.Add(this.chkUseHsl);
            this.Controls.Add(this.lampStatus);
            this.Name = "UcConnectionPanel";
            this.Size = new System.Drawing.Size(860, 36);
            this.ResumeLayout(false);
            this.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.nudRack)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudSlot)).EndInit();
        }

        #endregion
    }
}
