using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ZL.Iot.Controls.Controls
{
    public class UcReadWrite : UserControl
    {
        public event Action<string, string>? ReadRequested; // address, type
        public event Action<string, string, string>? WriteRequested; // address, type, value

        private FlowLayoutPanel rbFlow;
        private TextBox txtAddress;
        private TextBox txtReadResult;
        private TextBox txtWriteValue;
        private Button btnRead;
        private Button btnWrite;
        private Button btnClear;

        public UcReadWrite()
        {
            Initialize();
        }

        private void Initialize()
        {
            this.Dock = DockStyle.Fill;
            this.BackColor = System.Drawing.Color.White;

            var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, AutoSize = true };
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // types
            var types = new[] { "bit", "byte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "string" };
            var grp = new GroupBox { Text = "类型", Dock = DockStyle.Fill, Padding = new Padding(6), AutoSize = true };
            rbFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            foreach (var t in types)
            {
                var rb = new RadioButton { Text = t, AutoSize = true, Margin = new Padding(6, 6, 12, 6) };
                rbFlow.Controls.Add(rb);
            }
            // default select string
            foreach (RadioButton r in rbFlow.Controls.OfType<RadioButton>()) r.Checked = r.Text == "string";
            grp.Controls.Add(rbFlow);

            // address and operations
            var op = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, AutoSize = true };
            op.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            op.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            op.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            op.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            op.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            op.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var lblAddr = new Label { Text = "地址:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6) };
            txtAddress = new TextBox { Text = "DB1.DBD0", Dock = DockStyle.Fill, Margin = new Padding(6) };
            btnRead = new Button { Text = "读", AutoSize = true, Margin = new Padding(6) };
            txtReadResult = new TextBox { Width = 120, ReadOnly = true, Margin = new Padding(6) };
            btnWrite = new Button { Text = "写入", AutoSize = true, Margin = new Padding(6) };
            var lblWrite = new Label { Text = "写入值:", AutoSize = true, Margin = new Padding(6) };
            txtWriteValue = new TextBox { Width = 120, Margin = new Padding(6) };
            var chkShow = new CheckBox { Text = "显示报文", AutoSize = true, Margin = new Padding(6) };

            btnClear = new Button { Text = "清空数据", AutoSize = true, Margin = new Padding(6) };

            op.Controls.Add(lblAddr, 0, 0);
            op.Controls.Add(txtAddress, 1, 0);
            op.Controls.Add(txtReadResult, 2, 0);
            op.Controls.Add(btnRead, 3, 0);
            op.Controls.Add(lblWrite, 4, 0);
            op.Controls.Add(txtWriteValue, 5, 0);
            op.Controls.Add(btnWrite, 6, 0);
            op.Controls.Add(chkShow, 7, 0);
            op.Controls.Add(btnClear, 8, 0);

            main.Controls.Add(grp, 0, 0);
            main.Controls.Add(op, 0, 1);

            this.Controls.Add(main);

            btnRead.Click += (s, e) => OnRead();
            btnWrite.Click += (s, e) => OnWrite();
            btnClear.Click += (s, e) => { txtReadResult.Clear(); txtWriteValue.Clear(); };
        }

        private string GetSelectedType()
        {
            foreach (RadioButton r in rbFlow.Controls.OfType<RadioButton>()) if (r.Checked) return r.Text;
            return "string";
        }

        private void OnRead()
        {
            var addr = txtAddress.Text.Trim();
            var type = GetSelectedType();
            ReadRequested?.Invoke(addr, type);
        }

        private void OnWrite()
        {
            var addr = txtAddress.Text.Trim();
            var type = GetSelectedType();
            var val = txtWriteValue.Text.Trim();
            WriteRequested?.Invoke(addr, type, val);
        }

        public void SetReadResult(string result)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(() => txtReadResult.Text = result)); return; }
            txtReadResult.Text = result;
        }
    }
}
