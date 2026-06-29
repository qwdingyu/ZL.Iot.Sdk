namespace ZL.Iot.Controls.Controls
{
    partial class UcTagTable
    {
        /// <summary> 
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        /// <summary> 
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle5 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle6 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle7 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
            this.dgv_Tags = new System.Windows.Forms.DataGridView();
            this.Enable = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.Column_name = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column_address = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column_type = new System.Windows.Forms.DataGridViewComboBoxColumn();
            this.Column_encoding = new System.Windows.Forms.DataGridViewComboBoxColumn();
            this.Column_length = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column_value = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column_unit = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column_decs = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.TSMI_del = new System.Windows.Forms.ToolStripMenuItem();
            this.TSMI_Save = new System.Windows.Forms.ToolStripMenuItem();
            this.TSMI_Write = new System.Windows.Forms.ToolStripMenuItem();
            this.textBox_time = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.btn_Refresh = new System.Windows.Forms.Button();
            this.btn_out_clip = new System.Windows.Forms.Button();
            this.btn_from_clip = new System.Windows.Forms.Button();
            this.btn_out_file = new System.Windows.Forms.Button();
            this.btn_from_file = new System.Windows.Forms.Button();
            this.label_info = new System.Windows.Forms.Label();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.ck_AllTags = new System.Windows.Forms.CheckBox();
            this.btn_ClearTagDgv = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.dgv_Tags)).BeginInit();
            this.contextMenuStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.SuspendLayout();
            // 
            // dgv_Tags
            // 
            this.dgv_Tags.AllowUserToResizeRows = false;
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle1.BackColor = System.Drawing.Color.AliceBlue;
            this.dgv_Tags.AlternatingRowsDefaultCellStyle = dataGridViewCellStyle1;
            this.dgv_Tags.BackgroundColor = System.Drawing.Color.White;
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle2.BackColor = System.Drawing.SystemColors.Control;
            dataGridViewCellStyle2.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            dataGridViewCellStyle2.ForeColor = System.Drawing.SystemColors.WindowText;
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle2.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.dgv_Tags.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle2;
            this.dgv_Tags.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgv_Tags.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.Enable,
            this.Column_name,
            this.Column_address,
            this.Column_type,
            this.Column_encoding,
            this.Column_length,
            this.Column_value,
            this.Column_unit,
            this.Column_decs});
            this.dgv_Tags.ContextMenuStrip = this.contextMenuStrip1;
            dataGridViewCellStyle5.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle5.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle5.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            dataGridViewCellStyle5.ForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle5.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle5.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle5.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
            this.dgv_Tags.DefaultCellStyle = dataGridViewCellStyle5;
            this.dgv_Tags.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgv_Tags.Location = new System.Drawing.Point(0, 0);
            this.dgv_Tags.Name = "dgv_Tags";
            dataGridViewCellStyle6.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle6.BackColor = System.Drawing.SystemColors.Control;
            dataGridViewCellStyle6.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            dataGridViewCellStyle6.ForeColor = System.Drawing.SystemColors.WindowText;
            dataGridViewCellStyle6.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle6.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle6.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.dgv_Tags.RowHeadersDefaultCellStyle = dataGridViewCellStyle6;
            this.dgv_Tags.RowHeadersWidth = 40;
            dataGridViewCellStyle7.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.dgv_Tags.RowsDefaultCellStyle = dataGridViewCellStyle7;
            this.dgv_Tags.RowTemplate.Height = 23;
            this.dgv_Tags.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgv_Tags.Size = new System.Drawing.Size(927, 324);
            this.dgv_Tags.TabIndex = 0;
            this.dgv_Tags.CellValueChanged += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgv_Tags_CellValueChanged);
            this.dgv_Tags.CurrentCellDirtyStateChanged += new System.EventHandler(this.dgv_Tags_CurrentCellDirtyStateChanged);
            // 
            // Enable
            // 
            this.Enable.DataPropertyName = "Enable";
            this.Enable.FillWeight = 182.7411F;
            this.Enable.HeaderText = "选中";
            this.Enable.Name = "Enable";
            this.Enable.Width = 40;
            // 
            // Column_name
            // 
            this.Column_name.FillWeight = 89.65736F;
            this.Column_name.HeaderText = "标签";
            this.Column_name.MinimumWidth = 6;
            this.Column_name.Name = "Column_name";
            this.Column_name.Width = 106;
            // 
            // Column_address
            // 
            this.Column_address.FillWeight = 89.65736F;
            this.Column_address.HeaderText = "设备地址";
            this.Column_address.MinimumWidth = 6;
            this.Column_address.Name = "Column_address";
            this.Column_address.Width = 105;
            // 
            // Column_type
            // 
            dataGridViewCellStyle3.BackColor = System.Drawing.Color.Transparent;
            this.Column_type.DefaultCellStyle = dataGridViewCellStyle3;
            this.Column_type.FillWeight = 89.65736F;
            this.Column_type.HeaderText = "数据类型";
            this.Column_type.Items.AddRange(new object[] {
            "bool",
            "byte",
            "short",
            "ushort",
            "int",
            "uint",
            "long",
            "ulong",
            "float",
            "double",
            "string",
            "byte[]"});
            this.Column_type.MaxDropDownItems = 12;
            this.Column_type.MinimumWidth = 6;
            this.Column_type.Name = "Column_type";
            this.Column_type.Width = 106;
            // 
            // Column_encoding
            // 
            this.Column_encoding.FillWeight = 89.65736F;
            this.Column_encoding.HeaderText = "字符编码";
            this.Column_encoding.Items.AddRange(new object[] {
            "Default",
            "ASCII",
            "Unicode",
            "UTF-8",
            "GB2312"});
            this.Column_encoding.MinimumWidth = 6;
            this.Column_encoding.Name = "Column_encoding";
            this.Column_encoding.Width = 106;
            // 
            // Column_length
            // 
            this.Column_length.FillWeight = 89.65736F;
            this.Column_length.HeaderText = "长度";
            this.Column_length.MinimumWidth = 6;
            this.Column_length.Name = "Column_length";
            this.Column_length.ToolTipText = "数组长度，大于0表示这是一个数组，并且指示长度信息";
            this.Column_length.Width = 105;
            // 
            // Column_value
            // 
            dataGridViewCellStyle4.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            this.Column_value.DefaultCellStyle = dataGridViewCellStyle4;
            this.Column_value.FillWeight = 89.65736F;
            this.Column_value.HeaderText = "值";
            this.Column_value.MinimumWidth = 6;
            this.Column_value.Name = "Column_value";
            this.Column_value.Width = 106;
            // 
            // Column_unit
            // 
            this.Column_unit.FillWeight = 89.65736F;
            this.Column_unit.HeaderText = "单位";
            this.Column_unit.MinimumWidth = 6;
            this.Column_unit.Name = "Column_unit";
            this.Column_unit.Width = 105;
            // 
            // Column_decs
            // 
            this.Column_decs.FillWeight = 89.65736F;
            this.Column_decs.HeaderText = "注释";
            this.Column_decs.MinimumWidth = 6;
            this.Column_decs.Name = "Column_decs";
            this.Column_decs.Width = 106;
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.TSMI_del,
            this.TSMI_Save,
            this.TSMI_Write});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(101, 70);
            // 
            // TSMI_del
            // 
            this.TSMI_del.Name = "TSMI_del";
            this.TSMI_del.Size = new System.Drawing.Size(100, 22);
            this.TSMI_del.Text = "删除";
            this.TSMI_del.Click += new System.EventHandler(this.ToolStripMenuItem_Click);
            // 
            // TSMI_Save
            // 
            this.TSMI_Save.Name = "TSMI_Save";
            this.TSMI_Save.Size = new System.Drawing.Size(100, 22);
            this.TSMI_Save.Text = "保存";
            this.TSMI_Save.Click += new System.EventHandler(this.ToolStripMenuItem_Click);
            // 
            // TSMI_Write
            // 
            this.TSMI_Write.Name = "TSMI_Write";
            this.TSMI_Write.Size = new System.Drawing.Size(100, 22);
            this.TSMI_Write.Text = "写入";
            this.TSMI_Write.Click += new System.EventHandler(this.ToolStripMenuItem_Click);
            // 
            // textBox_time
            // 
            this.textBox_time.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.textBox_time.Location = new System.Drawing.Point(724, 8);
            this.textBox_time.Name = "textBox_time";
            this.textBox_time.Size = new System.Drawing.Size(84, 23);
            this.textBox_time.TabIndex = 1;
            this.textBox_time.Text = "200";
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(645, 11);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(59, 17);
            this.label1.TabIndex = 2;
            this.label1.Text = "间隔时间:";
            // 
            // btn_Refresh
            // 
            this.btn_Refresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btn_Refresh.Location = new System.Drawing.Point(814, 6);
            this.btn_Refresh.Name = "btn_Refresh";
            this.btn_Refresh.Size = new System.Drawing.Size(101, 27);
            this.btn_Refresh.TabIndex = 3;
            this.btn_Refresh.Text = "开始刷新";
            this.btn_Refresh.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btn_Refresh.UseVisualStyleBackColor = true;
            // 
            // btn_out_clip
            // 
            this.btn_out_clip.Location = new System.Drawing.Point(7, 6);
            this.btn_out_clip.Name = "btn_out_clip";
            this.btn_out_clip.Size = new System.Drawing.Size(101, 27);
            this.btn_out_clip.TabIndex = 4;
            this.btn_out_clip.Text = "导出到剪切板";
            this.btn_out_clip.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btn_out_clip.UseVisualStyleBackColor = true;
            this.btn_out_clip.Click += new System.EventHandler(this.button_out_clip_Click);
            // 
            // btn_from_clip
            // 
            this.btn_from_clip.Location = new System.Drawing.Point(115, 6);
            this.btn_from_clip.Name = "btn_from_clip";
            this.btn_from_clip.Size = new System.Drawing.Size(101, 27);
            this.btn_from_clip.TabIndex = 5;
            this.btn_from_clip.Text = "从剪切板导入";
            this.btn_from_clip.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btn_from_clip.UseVisualStyleBackColor = true;
            this.btn_from_clip.Click += new System.EventHandler(this.button_from_clip_Click);
            // 
            // btn_out_file
            // 
            this.btn_out_file.Location = new System.Drawing.Point(226, 6);
            this.btn_out_file.Name = "btn_out_file";
            this.btn_out_file.Size = new System.Drawing.Size(101, 27);
            this.btn_out_file.TabIndex = 6;
            this.btn_out_file.Text = "导出到文件";
            this.btn_out_file.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btn_out_file.UseVisualStyleBackColor = true;
            this.btn_out_file.Click += new System.EventHandler(this.button_out_file_Click);
            // 
            // btn_from_file
            // 
            this.btn_from_file.Location = new System.Drawing.Point(336, 6);
            this.btn_from_file.Name = "btn_from_file";
            this.btn_from_file.Size = new System.Drawing.Size(101, 27);
            this.btn_from_file.TabIndex = 7;
            this.btn_from_file.Text = "从文件导入";
            this.btn_from_file.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btn_from_file.UseVisualStyleBackColor = true;
            this.btn_from_file.Click += new System.EventHandler(this.button_from_file_Click);
            // 
            // label_info
            // 
            this.label_info.AutoSize = true;
            this.label_info.ForeColor = System.Drawing.Color.Gray;
            this.label_info.Location = new System.Drawing.Point(447, 6);
            this.label_info.Name = "label_info";
            this.label_info.Size = new System.Drawing.Size(0, 17);
            this.label_info.TabIndex = 8;
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitContainer1.IsSplitterFixed = true;
            this.splitContainer1.Location = new System.Drawing.Point(0, 0);
            this.splitContainer1.Name = "splitContainer1";
            this.splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.ck_AllTags);
            this.splitContainer1.Panel1.Controls.Add(this.btn_ClearTagDgv);
            this.splitContainer1.Panel1.Controls.Add(this.btn_Refresh);
            this.splitContainer1.Panel1.Controls.Add(this.textBox_time);
            this.splitContainer1.Panel1.Controls.Add(this.btn_from_file);
            this.splitContainer1.Panel1.Controls.Add(this.label1);
            this.splitContainer1.Panel1.Controls.Add(this.btn_out_file);
            this.splitContainer1.Panel1.Controls.Add(this.btn_out_clip);
            this.splitContainer1.Panel1.Controls.Add(this.btn_from_clip);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.dgv_Tags);
            this.splitContainer1.Size = new System.Drawing.Size(927, 368);
            this.splitContainer1.SplitterDistance = 74;
            this.splitContainer1.TabIndex = 9;
            // 
            // ck_AllTags
            // 
            this.ck_AllTags.AutoSize = true;
            this.ck_AllTags.Location = new System.Drawing.Point(451, 9);
            this.ck_AllTags.Name = "ck_AllTags";
            this.ck_AllTags.Size = new System.Drawing.Size(47, 21);
            this.ck_AllTags.TabIndex = 9;
            this.ck_AllTags.Text = "ALL";
            this.ck_AllTags.UseVisualStyleBackColor = true;
            // 
            // btn_ClearTagDgv
            // 
            this.btn_ClearTagDgv.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btn_ClearTagDgv.Location = new System.Drawing.Point(570, 6);
            this.btn_ClearTagDgv.Name = "btn_ClearTagDgv";
            this.btn_ClearTagDgv.Size = new System.Drawing.Size(69, 27);
            this.btn_ClearTagDgv.TabIndex = 8;
            this.btn_ClearTagDgv.Text = "清空点表";
            this.btn_ClearTagDgv.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btn_ClearTagDgv.UseVisualStyleBackColor = true;
            this.btn_ClearTagDgv.Click += new System.EventHandler(this.btn_ClearTagDgv_Click);
            // 
            // UcTagTable
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.label_info);
            this.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Name = "UcTagTable";
            this.Size = new System.Drawing.Size(927, 368);
            this.Load += new System.EventHandler(this.DataTableControl_Load);
            ((System.ComponentModel.ISupportInitialize)(this.dgv_Tags)).EndInit();
            this.contextMenuStrip1.ResumeLayout(false);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel1.PerformLayout();
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.DataGridView dgv_Tags;
        private System.Windows.Forms.TextBox textBox_time;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button btn_Refresh;
        private System.Windows.Forms.Button btn_out_clip;
        private System.Windows.Forms.Button btn_from_clip;
        private System.Windows.Forms.Button btn_out_file;
        private System.Windows.Forms.Button btn_from_file;
        private System.Windows.Forms.Label label_info;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.Button btn_ClearTagDgv;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem TSMI_del;
        private System.Windows.Forms.ToolStripMenuItem TSMI_Save;
        private System.Windows.Forms.ToolStripMenuItem TSMI_Write;
        private System.Windows.Forms.CheckBox ck_AllTags;
        private System.Windows.Forms.DataGridViewCheckBoxColumn Enable;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column_name;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column_address;
        private System.Windows.Forms.DataGridViewComboBoxColumn Column_type;
        private System.Windows.Forms.DataGridViewComboBoxColumn Column_encoding;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column_length;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column_value;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column_unit;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column_decs;
    }
}
