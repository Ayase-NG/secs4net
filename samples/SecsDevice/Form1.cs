using Microsoft.Extensions.Options;
using Secs4Net;
using Secs4Net.Sml;
using SECSparser;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using SECShandler.Interfaces;
using SECShandler.Handlers;
using SECShandler.Functions;
using System.Threading.Tasks;

namespace SecsDevice;

public partial class Form1 : Form
{
    private SecsGem? _secsGem;
    private HsmsConnection? _connector;
    private readonly ISecsGemLogger _logger;
    private readonly BindingList<PrimaryMessageWrapper> recvBuffer = new();
    private CancellationTokenSource _cancellationTokenSource = new();

    public Form1()
    {
        InitializeComponent();

        radioActiveMode.DataBindings.Add("Enabled", btnEnable, "Enabled");
        radioPassiveMode.DataBindings.Add("Enabled", btnEnable, "Enabled");
        txtAddress.DataBindings.Add("Enabled", btnEnable, "Enabled");
        numPort.DataBindings.Add("Enabled", btnEnable, "Enabled");
        numDeviceId.DataBindings.Add("Enabled", btnEnable, "Enabled");
        numBufferSize.DataBindings.Add("Enabled", btnEnable, "Enabled");
        recvMessageBindingSource.DataSource = recvBuffer;
        Application.ThreadException += (sender, e) => MessageBox.Show(e.Exception.ToString());
        AppDomain.CurrentDomain.UnhandledException += (sender, e) => MessageBox.Show(e.ExceptionObject.ToString());
        _logger = new SecsLogger(this);
    }

    // 简单的内存实现，仅用于 sample 测试
    private class InMemoryReportStorage : IReportStorage
    {
        private readonly Dictionary<uint, List<uint>> _reports = new();
        public void AddOrUpdateReport(uint rptId, List<uint> vidList) => _reports[rptId] = vidList;
        public bool ContainsReport(uint rptId) => _reports.ContainsKey(rptId);
        public void ClearAllReports() => _reports.Clear();
        public void RemoveReport(uint rptId) => _reports.Remove(rptId);
    }

    private class InMemoryEventLinkStorage : IEventLinkStorage
    {
        private readonly Dictionary<uint, List<uint>> _links = new();
        public bool IsCeidValid(uint ceid) => true; // 对 sample 直接返回 true
        public IReadOnlyList<uint> GetRptIdsForCeid(uint ceid) => _links.TryGetValue(ceid, out var v) ? v : new List<uint>();
        public void UpdateEventLinks(Dictionary<uint, List<uint>> links) { _links.Clear(); foreach (var kv in links) _links[kv.Key] = kv.Value; }
        public void UnlinkEvent(uint ceid) => _links.Remove(ceid);
    }

    private class InMemoryEventEnableStorage : IEventEnableStorage
    {
        private readonly HashSet<uint> _enabled = new();
        public void EnableAllEvents() { /* not implemented for sample */ }
        public void EnableEvent(uint ceid) => _enabled.Add(ceid);
        public void DisableEvent(uint ceid) => _enabled.Remove(ceid);
        public bool IsEventEnabled(uint ceid) => _enabled.Contains(ceid);
        public IReadOnlyList<uint> GetAllEnabledEvents() => _enabled.ToList();
    }

    private static async Task ReplyNotSupported(PrimaryMessageWrapper primary)
    {
        // 构造 S9F7 (Not Supported) 回复
        var reply = new SecsMessage(9, 7, replyExpected: false) { Name = "NotSupported", SecsItem = Item.L() };
        try
        {
            await primary.TryReplyAsync(reply);
        }
        catch
        {
            // ignore
        }
    }

    private async void btnEnable_Click(object sender, EventArgs e)
    {
        _secsGem?.Dispose();

        if (_connector is not null)
        {
            await _connector.DisposeAsync();
        }

        var options = Options.Create(new SecsGemOptions
        {
            IsActive = radioActiveMode.Checked,
            IpAddress = txtAddress.Text,
            Port = (int)numPort.Value,
            SocketReceiveBufferSize = (int)numBufferSize.Value,
            DeviceId = (ushort)numDeviceId.Value,
        });

        _connector = new HsmsConnection(options, _logger);
        _secsGem = new SecsGem(options, _connector, _logger);

        _connector.ConnectionChanged += delegate
        {
            base.Invoke((MethodInvoker)delegate
            {
                lbStatus.Text = _connector.State.ToString();
            });
        };

        btnEnable.Enabled = false;
        _connector.Start(_cancellationTokenSource.Token);
        btnDisable.Enabled = true;

        var testDevice = new TestDevice();

        var reportStorage = new InMemoryReportStorage();
        var eventLinkStorage = new InMemoryEventLinkStorage();
        var eventEnableStorage = new InMemoryEventEnableStorage();

        var primaryHandlers = new List<IPrimaryMessageHandler>
        {
            new CommunicationPrimaryMessageHandler(testDevice),
            new EventReportPrimaryMessageHandler(reportStorage, eventLinkStorage, eventEnableStorage),
        };

        var routes = new Dictionary<(int S, int F), IPrimaryMessageHandler>();
        foreach (var handler in primaryHandlers)
        {
            foreach (var sf in handler.SupportedMessages)
            {
                routes[sf] = handler;
            }
        }

        try
        {
            await foreach (var primaryMessage in _secsGem.GetPrimaryMessageAsync(_cancellationTokenSource.Token))
            {
                recvBuffer.Add(primaryMessage);
                var msg = primaryMessage.PrimaryMessage;
                Console.WriteLine($"收到消息: S{msg.S}F{msg.F}");

                try
                {
                    switch ((msg.S, msg.F))
                    {
                        case (1, 13):
                            await CommunicationSxFyFunctions.HandleS1F13ReplyAsync(_secsGem, testDevice, primaryMessage);
                            break;

                        case (1, 1):
                            await CommunicationSxFyFunctions.HandleS1F1ReplyAsync(_secsGem, testDevice, primaryMessage);
                            break;

                        case (2, 33):
                            await EventReportSxFyFunctions.HandleS2F33ReplyAsync(primaryMessage, reportStorage);
                            break;
                        case (2, 35):
                            await EventReportSxFyFunctions.HandleS2F35ReplyAsync(primaryMessage, reportStorage, eventLinkStorage);
                            break;
                        case (2, 37):
                            await EventReportSxFyFunctions.HandleS2F37ReplyAsync(primaryMessage, eventLinkStorage, eventEnableStorage);
                            break;

                        default:
                            if (routes.TryGetValue((msg.S, msg.F), out var routeHandler))
                            {
                                await routeHandler.HandleAsync(_secsGem, primaryMessage, _cancellationTokenSource.Token);
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理消息时出错: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {

        }
    }

    private async void btnDisable_Click(object sender, EventArgs e)
    {
        if (!_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }
        if (_connector is not null)
        {
            await _connector.DisposeAsync();
        }
        _secsGem?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();

        _secsGem = null;
        btnEnable.Enabled = true;
        btnDisable.Enabled = false;
        lbStatus.Text = "Disable";
        recvBuffer.Clear();
        richTextBox1.Clear();
    }

    private async void btnSendPrimary_Click(object sender, EventArgs e)
    {
        if (_secsGem is null || string.IsNullOrWhiteSpace(txtSendPrimary.Text) || _connector?.State != ConnectionState.Selected)
        {
            return;
        }

        try
        {
            var reply = await _secsGem.SendAsync(txtSendPrimary.Text.ToSecsMessage(), _cancellationTokenSource.Token);
            txtRecvSecondary.Text = reply.ToSml();
        }
        catch (SecsException ex)
        {
            txtRecvSecondary.Text = ex.Message;
        }
    }

    private void lstUnreplyMsg_SelectedIndexChanged(object sender, EventArgs e)
    {
        var receivedMessage = lstUnreplyMsg.SelectedItem as PrimaryMessageWrapper;
        txtRecvPrimary.Text = receivedMessage?.PrimaryMessage.ToSml();
    }

    private async void btnReplySecondary_Click(object sender, EventArgs e)
    {
        if (lstUnreplyMsg.SelectedItem is not PrimaryMessageWrapper recv
            || string.IsNullOrWhiteSpace(txtReplySeconary.Text))
        {
            return;
        }

        await recv.TryReplyAsync(txtReplySeconary.Text.ToSecsMessage(), _cancellationTokenSource.Token);
        recvBuffer.Remove(recv);
        txtRecvPrimary.Clear();
    }

    private async void btnReplyS9F7_Click(object sender, EventArgs e)
    {
        if (lstUnreplyMsg.SelectedItem is not PrimaryMessageWrapper recv)
        {
            return;
        }

        await recv.TryReplyAsync();

        recvBuffer.Remove(recv);
        txtRecvPrimary.Clear();
    }

    private sealed class SecsLogger : ISecsGemLogger
    {
        private readonly Form1 _form;
        internal SecsLogger(Form1 form)
        {
            _form = form;
        }

        public void MessageIn(SecsMessage msg, int id)
        {
            _form.Invoke((MethodInvoker)delegate
            {
                _form.richTextBox1.SelectionColor = Color.Black;
                _form.richTextBox1.AppendText($"<-- [0x{id:X8}] {msg.ToSml()}\n");
            });
        }

        public void MessageOut(SecsMessage msg, int id)
        {
            _form.Invoke((MethodInvoker)delegate
            {
                _form.richTextBox1.SelectionColor = Color.Black;
                _form.richTextBox1.AppendText($"--> [0x{id:X8}] {msg.ToSml()}\n");
            });
        }

        public void Info(string msg)
        {
            _form.Invoke((MethodInvoker)delegate
            {
                _form.richTextBox1.SelectionColor = Color.Blue;
                _form.richTextBox1.AppendText($"{msg}\n");
            });
        }

        public void Warning(string msg)
        {
            _form.Invoke((MethodInvoker)delegate
            {
                _form.richTextBox1.SelectionColor = Color.Green;
                _form.richTextBox1.AppendText($"{msg}\n");
            });
        }

        public void Error(string msg, SecsMessage? message, Exception? ex)
        {
            _form.Invoke((MethodInvoker)delegate
            {
                _form.richTextBox1.SelectionColor = Color.Red;
                _form.richTextBox1.AppendText($"{msg}\n");
                _form.richTextBox1.AppendText($"{message?.ToSml()}\n");
                _form.richTextBox1.SelectionColor = Color.Gray;
                _form.richTextBox1.AppendText($"{ex}\n");
            });
        }

        public void Debug(string msg)
        {
            _form.Invoke((MethodInvoker)delegate
            {
                _form.richTextBox1.SelectionColor = Color.Yellow;
                _form.richTextBox1.AppendText($"{msg}\n");
            });
        }

#if NET472
        public void Error(string msg)
        {
            Error(msg, null, null);
        }

        public void Error(string msg, Exception ex)
        {
            Error(msg, null, ex);
        }
#endif
    }
}

internal class TestDevice : IDevice
{
    public bool IsOnline { get; set; }

    public string ModelNumber { get; set; }
    public string SoftwareRevision { get; set; }
    public TestDevice()
    {
        IsOnline = true;
        ModelNumber = "GWM-PW-20260407";
        SoftwareRevision = "V20260407";
    }

    public Task StartProcessAsync(string? lotId)
    {
        return Task.CompletedTask;
    }
    public Task StopProcessAsync() => Task.CompletedTask;
    public Task PauseProcessAsync() => Task.CompletedTask;
    public Task ResumeProcessAsync() => Task.CompletedTask;
    public Task AbortProcessAsync() => Task.CompletedTask;
    public Task PPSelectAsync() => Task.CompletedTask;
    public Task ChangeToLocalAsync() => Task.CompletedTask;
    public Task LoadCarrierAsync() => Task.CompletedTask;
    public Task UnLoadCarrierAsync() => Task.CompletedTask;
}
