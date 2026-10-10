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
        _address = new TextBox();
        _portLabel = new Label();
        _port = new NumericUpDown();
        _deviceIdLabel = new Label();
        _deviceId = new NumericUpDown();
        _active = new RadioButton();
        _passive = new RadioButton();
        _start = new Button();
        _stop = new Button();
        _statusLabel = new Label();
        _status = new Label();
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
        _header.Location = new Point(0, 0);
        _header.Margin = new Padding(5, 5, 5, 5);
        _header.Name = "_header";
        _header.Size = new Size(1540, 154);
        _header.TabIndex = 2;
        // 
        // _addressLabel
        // 
        _addressLabel.AutoSize = true;
        _addressLabel.Location = new Point(19, 29);
        _addressLabel.Margin = new Padding(5, 0, 5, 0);
        _addressLabel.Name = "_addressLabel";
        _addressLabel.Size = new Size(26, 24);
        _addressLabel.TabIndex = 0;
        _addressLabel.Text = "IP";
        // 
        // _address
        // 
        _address.Location = new Point(68, 22);
        _address.Margin = new Padding(5, 5, 5, 5);
        _address.Name = "_address";
        _address.Size = new Size(249, 30);
        _address.TabIndex = 1;
        // 
        // _portLabel
        // 
        _portLabel.AutoSize = true;
        _portLabel.Location = new Point(346, 29);
        _portLabel.Margin = new Padding(5, 0, 5, 0);
        _portLabel.Name = "_portLabel";
        _portLabel.Size = new Size(46, 24);
        _portLabel.TabIndex = 2;
        _portLabel.Text = "Port";
        // 
        // _port
        // 
        _port.Location = new Point(409, 22);
        _port.Margin = new Padding(5, 5, 5, 5);
        _port.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
        _port.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _port.Name = "_port";
        _port.Size = new Size(118, 30);
        _port.TabIndex = 3;
        _port.Value = new decimal(new int[] { 1, 0, 0, 0 });
        // 
        // _deviceIdLabel
        // 
        _deviceIdLabel.AutoSize = true;
        _deviceIdLabel.Location = new Point(553, 29);
        _deviceIdLabel.Margin = new Padding(5, 0, 5, 0);
        _deviceIdLabel.Name = "_deviceIdLabel";
        _deviceIdLabel.Size = new Size(91, 24);
        _deviceIdLabel.TabIndex = 4;
        _deviceIdLabel.Text = "Device ID";
        // 
        // _deviceId
        // 
        _deviceId.Location = new Point(671, 22);
        _deviceId.Margin = new Padding(5, 5, 5, 5);
        _deviceId.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
        _deviceId.Name = "_deviceId";
        _deviceId.Size = new Size(118, 30);
        _deviceId.TabIndex = 5;
        // 
        // _active
        // 
        _active.AutoSize = true;
        _active.Location = new Point(830, 26);
        _active.Margin = new Padding(5, 5, 5, 5);
        _active.Name = "_active";
        _active.Size = new Size(88, 28);
        _active.TabIndex = 6;
        _active.Text = "Active";
        // 
        // _passive
        // 
        _passive.AutoSize = true;
        _passive.Location = new Point(970, 26);
        _passive.Margin = new Padding(5, 5, 5, 5);
        _passive.Name = "_passive";
        _passive.Size = new Size(96, 28);
        _passive.TabIndex = 7;
        _passive.Text = "Passive";
        // 
        // _start
        // 
        _start.Location = new Point(19, 83);
        _start.Margin = new Padding(5, 5, 5, 5);
        _start.Name = "_start";
        _start.Size = new Size(145, 48);
        _start.TabIndex = 8;
        _start.Text = "启动监听";
        _start.UseVisualStyleBackColor = true;
        // 
        // _stop
        // 
        _stop.Location = new Point(179, 83);
        _stop.Margin = new Padding(5, 5, 5, 5);
        _stop.Name = "_stop";
        _stop.Size = new Size(145, 48);
        _stop.TabIndex = 9;
        _stop.Text = "停止监听";
        _stop.UseVisualStyleBackColor = true;
        // 
        // _statusLabel
        // 
        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(358, 94);
        _statusLabel.Margin = new Padding(5, 0, 5, 0);
        _statusLabel.Name = "_statusLabel";
        _statusLabel.Size = new Size(86, 24);
        _statusLabel.TabIndex = 10;
        _statusLabel.Text = "连接状态:";
        // 
        // _status
        // 
        _status.AutoSize = true;
        _status.Location = new Point(479, 94);
        _status.Margin = new Padding(5, 0, 5, 0);
        _status.Name = "_status";
        _status.Size = new Size(84, 24);
        _status.TabIndex = 11;
        _status.Text = "Stopped";
        // 
        // _smlLog
        // 
        _smlLog.Dock = DockStyle.Fill;
        _smlLog.Font = new Font("Consolas", 9F);
        _smlLog.Location = new Point(0, 154);
        _smlLog.Margin = new Padding(5, 5, 5, 5);
        _smlLog.Name = "_smlLog";
        _smlLog.ReadOnly = true;
        _smlLog.Size = new Size(1540, 870);
        _smlLog.TabIndex = 1;
        _smlLog.Text = "";
        // 
        // SecsForm
        // 
        AutoScaleDimensions = new SizeF(11F, 24F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1540, 1024);
        Controls.Add(_smlLog);
        Controls.Add(_header);
        Margin = new Padding(5, 5, 5, 5);
        MinimumSize = new Size(1135, 526);
        Name = "SecsForm";
        Text = "SECS/HSMS 监听测试";
        _header.ResumeLayout(false);
        _header.PerformLayout();
        ((ISupportInitialize)_port).EndInit();
        ((ISupportInitialize)_deviceId).EndInit();
        ResumeLayout(false);
    }

    #endregion
}
