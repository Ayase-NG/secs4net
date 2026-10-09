using Secs4Net;
using SECSGrpcService.Services;
using System.Net;
using System.Windows.Forms;

namespace SECSGrpcService.Views;

/// <summary>
/// SECS/HSMS 调试窗体：复用后台监听器，设置连接参数并查看状态和 SML 收发日志。
/// </summary>
public sealed partial class SecsForm : Form
{
    private readonly SecsPrimaryMessageListenerService _listener = null!;
    private readonly IConfiguration _configuration = null!;

    /// <summary>供 WinForms 设计器创建窗体；实际运行使用带服务参数的构造函数。</summary>
    public SecsForm()
    {
        InitializeComponent();
        // 订阅 FormClosing 事件，防止误触关闭
        this.FormClosing += MainForm_FormClosing;
    }

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        // 只拦截用户点击右上角关闭按钮的情况
        if (e.CloseReason == CloseReason.UserClosing)
        {
            DialogResult result = MessageBox.Show(
                "确定要关闭窗口吗？",
                "确认关闭",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                // 用户选择“否”，取消关闭
                e.Cancel = true;
            }
        }
    }

    /// <summary>创建测试窗体并从现有 SecsListener 配置加载初始连接参数。</summary>
    public SecsForm(SecsPrimaryMessageListenerService listener, IConfiguration configuration) : this()
    {
        _listener = listener;
        _configuration = configuration;

        var options = SecsPrimaryMessageListenerService.CreateOptions(configuration.GetSection("SecsListener"));
        _address.Text = options.IpAddress;
        _port.Value = Math.Clamp(options.Port, (int)_port.Minimum, (int)_port.Maximum);
        _deviceId.Value = options.DeviceId;
        _active.Checked = options.IsActive;
        _passive.Checked = !options.IsActive;

        _status.Text = _listener.ConnectionStatus;
        UpdateButtons(_listener.IsListening);

        _listener.ConnectionStatusChanged += OnStatusChanged;
        _listener.SmlLogged += OnSmlLogged;
        _start.Click += StartClick;
        _stop.Click += StopClick;
        FormClosed += OnFormClosed;
    }

    /// <summary>按界面参数请求共享监听服务启动，不另行创建 SECS 连接。</summary>
    private async void StartClick(object? sender, EventArgs e)
    {
        // if 关键分支：无效 IP 不交给连接层，避免连接任务静默失败。
        if (!IPAddress.TryParse(_address.Text.Trim(), out _))
        {
            MessageBox.Show(this, "请输入有效的 IP 地址。", "SECS 监听", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var options = SecsPrimaryMessageListenerService.CreateOptions(_configuration.GetSection("SecsListener"));
        options.IpAddress = _address.Text.Trim();
        options.Port = (int)_port.Value;
        options.DeviceId = (ushort)_deviceId.Value;
        options.IsActive = _active.Checked;

        UpdateButtons(true);
        _status.Text = "Starting";
        try
        {
            // 异步调用说明：共享服务串行化启动请求，防止同时绑定相同端口。
            await _listener.StartListeningAsync(options);
        }
        catch (Exception ex)
        {
            UpdateButtons(false);
            _status.Text = _listener.ConnectionStatus;
            MessageBox.Show(this, ex.Message, "启动监听失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>停止共享 HSMS 监听，但保留 gRPC 服务运行。</summary>
    private async void StopClick(object? sender, EventArgs e)
    {
        _stop.Enabled = false;
        try
        {
            // 异步调用说明：等待共享连接的接收循环和网络资源完整释放。
            await _listener.StopListeningAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "停止监听失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateButtons(_listener.IsListening);
        }
    }

    /// <summary>将后台连接状态安全更新到 WinForms UI 线程。</summary>
    private void OnStatusChanged(string state)
    {
        PostToUi(() =>
        {
            _status.Text = state;
            UpdateButtons(_listener.IsListening);
        });
    }

    /// <summary>将后台收发 SML 限量输出到窗口，避免长时间运行无限占用内存。</summary>
    private void OnSmlLogged(string line)
    {
        PostToUi(() =>
        {
            if (_smlLog.TextLength > 200_000)
            {
                _smlLog.Clear();
            }
            _smlLog.AppendText($"{line}{Environment.NewLine}");
        });
    }

    /// <summary>跨线程调度 UI 更新，窗体关闭后不再访问控件。</summary>
    private void PostToUi(Action update)
    {
        // if 关键分支：窗体未创建或已关闭时丢弃后台事件。
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }
        try
        {
            BeginInvoke(update);
        }
        catch (InvalidOperationException)
        {
            // if 关键分支：关闭过程中窗口句柄被销毁时忽略未投递的事件。
        }
    }

    /// <summary>依据监听状态禁用冲突的输入和按钮。</summary>
    private void UpdateButtons(bool listening)
    {
        _start.Enabled = !listening;
        _stop.Enabled = listening;
        _address.Enabled = !listening;
        _port.Enabled = !listening;
        _deviceId.Enabled = !listening;
        _active.Enabled = !listening;
        _passive.Enabled = !listening;
    }

    /// <summary>窗体关闭时解除事件订阅，避免后台线程继续持有已销毁控件。</summary>
    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        _listener.ConnectionStatusChanged -= OnStatusChanged;
        _listener.SmlLogged -= OnSmlLogged;
    }
}
