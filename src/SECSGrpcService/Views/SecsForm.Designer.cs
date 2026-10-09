using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SECSGrpcService.Views;

partial class SecsForm
{
    private IContainer? components = null;
    private Panel _header = null!;
    private Label _addressLabel = null!;
    private Label _portLabel = null!;
    private Label _deviceIdLabel = null!;
    private Label _statusLabel = null!;
    private TextBox _address = null!;
    private NumericUpDown _port = null!;
    private NumericUpDown _deviceId = null!;
    private RadioButton _active = null!;
    private RadioButton _passive = null!;
    private Label _status = null!;
    private Button _start = null!;
    private Button _stop = null!;
    private RichTextBox _smlLog = null!;

    /// <summary>释放设计器创建的组件。</summary>
    protected override void Dispose(bool disposing)
    {
        // if 关键分支：窗体释放时同时清理设计器管理的组件。
        if (disposing)
        {
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>初始化 SECS 测试窗体的固定布局和控件。</summary>
    private void InitializeComponent()
    {
        _header = new Panel();
        _addressLabel = new Label();
        _portLabel = new Label();
        _deviceIdLabel = new Label();
        _statusLabel = new Label();
        _address = new TextBox();
        _port = new NumericUpDown();
        _deviceId = new NumericUpDown();
        _active = new RadioButton();
        _passive = new RadioButton();
        _status = new Label();
        _start = new Button();
        _stop = new Button();
        _smlLog = new RichTextBox();
        _header.SuspendLayout();
        ((ISupportInitialize)_port).BeginInit();
        ((ISupportInitialize)_deviceId).BeginInit();
        SuspendLayout();
        //
        // _header
        //
        _header.Controls.Add(_addressLabel);
        _header.Controls.Add(_address);
        _header.Controls.Add(_portLabel);
        _header.Controls.Add(_port);
        _header.Controls.Add(_deviceIdLabel);
        _header.Controls.Add(_deviceId);
        _header.Controls.Add(_active);
        _header.Controls.Add(_passive);
        _header.Controls.Add(_start);
        _header.Controls.Add(_stop);
        _header.Controls.Add(_statusLabel);
        _header.Controls.Add(_status);
        _header.Dock = DockStyle.Top;
        _header.Height = 96;
        _header.Name = "_header";
        //
        // _addressLabel
        //
        _addressLabel.AutoSize = true;
        _addressLabel.Location = new Point(12, 18);
        _addressLabel.Name = "_addressLabel";
        _addressLabel.Text = "IP";
        //
        // _address
        //
        _address.Location = new Point(43, 14);
        _address.Name = "_address";
        _address.Size = new Size(160, 23);
        //
        // _portLabel
        //
        _portLabel.AutoSize = true;
        _portLabel.Location = new Point(220, 18);
        _portLabel.Name = "_portLabel";
        _portLabel.Text = "Port";
        //
        // _port
        //
        _port.Location = new Point(260, 14);
        _port.Maximum = 65535;
        _port.Minimum = 1;
        _port.Name = "_port";
        _port.Size = new Size(75, 23);
        _port.Value = 1;
        //
        // _deviceIdLabel
        //
        _deviceIdLabel.AutoSize = true;
        _deviceIdLabel.Location = new Point(352, 18);
        _deviceIdLabel.Name = "_deviceIdLabel";
        _deviceIdLabel.Text = "Device ID";
        //
        // _deviceId
        //
        _deviceId.Location = new Point(427, 14);
        _deviceId.Maximum = 65535;
        _deviceId.Name = "_deviceId";
        _deviceId.Size = new Size(75, 23);
        //
        // _active
        //
        _active.AutoSize = true;
        _active.Location = new Point(528, 16);
        _active.Name = "_active";
        _active.Text = "Active";
        //
        // _passive
        //
        _passive.AutoSize = true;
        _passive.Location = new Point(617, 16);
        _passive.Name = "_passive";
        _passive.Text = "Passive";
        //
        // _start
        //
        _start.Location = new Point(12, 52);
        _start.Name = "_start";
        _start.Size = new Size(92, 30);
        _start.Text = "启动监听";
        _start.UseVisualStyleBackColor = true;
        //
        // _stop
        //
        _stop.Location = new Point(114, 52);
        _stop.Name = "_stop";
        _stop.Size = new Size(92, 30);
        _stop.Text = "停止监听";
        _stop.UseVisualStyleBackColor = true;
        //
        // _statusLabel
        //
        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(228, 59);
        _statusLabel.Name = "_statusLabel";
        _statusLabel.Text = "连接状态:";
        //
        // _status
        //
        _status.AutoSize = true;
        _status.Location = new Point(305, 59);
        _status.Name = "_status";
        _status.Text = "Stopped";
        //
        // _smlLog
        //
        _smlLog.Dock = DockStyle.Fill;
        _smlLog.Font = new Font("Consolas", 9F);
        _smlLog.Name = "_smlLog";
        _smlLog.ReadOnly = true;
        _smlLog.TabIndex = 1;
        //
        // SecsForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(980, 640);
        Controls.Add(_smlLog);
        Controls.Add(_header);
        MinimumSize = new Size(730, 350);
        Name = "SecsForm";
        Text = "SECS/HSMS 监听测试";
        ((ISupportInitialize)_port).EndInit();
        ((ISupportInitialize)_deviceId).EndInit();
        _header.ResumeLayout(false);
        _header.PerformLayout();
        ResumeLayout(false);
    }

    #endregion
}
