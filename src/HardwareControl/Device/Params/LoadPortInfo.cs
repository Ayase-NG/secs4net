using CommunityToolkit.Mvvm.ComponentModel;
using HardwareControl;

namespace GY.EFEM.Hardware.Device.Params;

/// <summary>
/// This class is used to store the information of the LoadPort.
/// </summary>
public partial class LoadPortInfo : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LoadPortInfo"/> class.
    /// The length of the inner array.
    /// </summary>
    public LoadPortInfo()
    {
        // 初始化外层数组
        this.CassetteIdFromPlc = new ushort[SystemConstParams.FoupNumber][];

        // 初始化每个内层数组（假设内层长度为 InnerArrayLength）
        for (int i = 0; i < SystemConstParams.FoupNumber; i++)
        {
            this.CassetteIdFromPlc[i] = new ushort[8];
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether LoadPort单工位停止.
    /// </summary>
    public bool LoadPortStopStatus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LoadPort单工位复位.
    /// </summary>
    public bool LoadPortResetStatus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LoadPort单工位启动.
    /// </summary>
    public bool LoadPortStartStatus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LP1命令开始.
    /// </summary>
    public bool LoadPort1CommandStart { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LP2命令开始.
    /// </summary>
    public bool LoadPort2CommandStart { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LP1执行中.
    /// </summary>
    public bool[] LoadPortOngoing { get; set; } = new bool[SystemConstParams.FoupNumber];

    /// <summary>
    /// Gets or sets a value indicating whether LP1Cassette有无.
    /// </summary>
    public bool LoadPort1HasCassette { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LP2Cassette有无.
    /// </summary>
    public bool LoadPort2HasCassette { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LP1错误.
    /// </summary>
    public ushort LoadPortErrCode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LP3命令开始.
    /// </summary>
    public bool LoadPort3CommandStart { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LP4命令开始.
    /// </summary>
    public bool LoadPort4CommandStart { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LP3Cassette有无.
    /// </summary>
    public bool LoadPort3HasCassette { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LP4Cassette有无.
    /// </summary>
    public bool LoadPort4HasCassette { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether mapping_cassette1Status.
    /// </summary>
    public bool[] MappingCassetteStatus { get; set; } = new bool[SystemConstParams.FoupNumber];

    /// <summary>
    /// Gets or sets a value indicating whether loadPort1CloseStatus.
    /// </summary>
    public bool[] LoadPortCloseStatus { get; set; } = new bool[SystemConstParams.FoupNumber];

    /// <summary>
    /// Gets or sets loadPortStatus.
    /// </summary>
    public bool[] LoadPortStatus { get; set; } = new bool[SystemConstParams.FoupNumber];

    /// <summary>
    /// Gets or sets cassetteSlotStatus.
    /// </summary>
    public ushort[] CassetteSlotStatus { get; set; } = new ushort[2 * SystemConstParams.FoupNumber];

    /// <summary>
    /// Gets or sets cassette1Id.
    /// </summary>
    public ushort[][] CassetteIdFromPlc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LP1任务号.
    /// </summary>
    [ObservableProperty]
    private ushort loadPort1Status;

    /// <summary>
    /// Gets or sets a value indicating whether LP2任务号.
    /// </summary>
    [ObservableProperty]
    private ushort loadPort2Status;

    /// <summary>
    /// Gets or sets a value indicating whether LP3任务号.
    /// </summary>
    [ObservableProperty]
    private ushort loadPort3Status;

    /// <summary>
    /// Gets or sets a value indicating whether LP4任务号.
    /// </summary>
    [ObservableProperty]
    private ushort loadPort4Status;

    /// <summary>
    /// Gets or sets a value indicating whether LP1任务号.
    /// </summary>
    public ushort LoadPort1Task { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LP2任务号.
    /// </summary>
    public ushort LoadPort2Task { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LP3任务号.
    /// </summary>
    public ushort LoadPort3Task { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LP4任务号.
    /// </summary>
    public ushort LoadPort4Task { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether LP1MP结果[1]+[2].
    /// </summary>
    public ushort[] LoadPort1MappingResult { get; set; } = new ushort[2 * SystemConstParams.FoupNumber];

    /// <summary>
    /// Gets or sets a value indicating whether LP2MP结果[1]+[2].
    /// </summary>
    public ushort[] LoadPort2MappingResult { get; set; } = new ushort[2 * SystemConstParams.FoupNumber];

    /// <summary>
    /// Gets or sets a value indicating whether LP1/SLOT选择[1]+[2].
    /// </summary>
    public ushort[] SelectLoadPort1Slot { get; set; } = new ushort[2 * SystemConstParams.FoupNumber];

    /// <summary>
    /// Gets or sets a value indicating whether LP2/SLOT选择[1]+[2].
    /// </summary>
    public ushort[] SelectLoadPort2Slot { get; set; } = new ushort[2 * SystemConstParams.FoupNumber];

    /// <summary>
    /// Gets or sets a value indicating whether LP3MP结果[1]+[2].
    /// </summary>
    public ushort[] LoadPort3MappingResult { get; set; } = new ushort[2 * SystemConstParams.FoupNumber];

    /// <summary>
    /// Gets or sets a value indicating whether LP4MP结果[1]+[2].
    /// </summary>
    public ushort[] LoadPort4MappingResult { get; set; } = new ushort[2 * SystemConstParams.FoupNumber];

    /// <summary>
    /// Gets or sets a value indicating whether LP3/SLOT选择[1]+[2].
    /// </summary>
    public ushort[] SelectLoadPort3Slot { get; set; } = new ushort[2 * SystemConstParams.FoupNumber];

    /// <summary>
    /// Gets or sets a value indicating whether LP4/SLOT选择[1]+[2].
    /// </summary>
    public ushort[] SelectLoadPort4Slot { get; set; } = new ushort[2 * SystemConstParams.FoupNumber];
}
