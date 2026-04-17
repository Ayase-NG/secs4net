using CommunityToolkit.Mvvm.ComponentModel;

namespace GY.EFEM.Hardware.Device.Params;

/// <summary>
/// This class is used to store the information of the aligner.
/// </summary>
[ObservableObject]
public partial class AlignerInfo : StationExternalInfo
{
    /// <summary>
    /// Gets or sets a value indicating whether Robot单工位停止.
    /// </summary>
    [ObservableProperty]
    private ushort status;

    // /// <summary>
    // /// Gets 当前检测的Plc上slotId.
    // /// </summary>
    // public override ushort PlcSlotId
    // {
    //     get => this.AlignerPlcSlotId;
    // }

    // /// <summary>
    // /// Gets 当前检测的Plc上slotId.
    // /// </summary>
    // public override ushort PlcCassetteId
    // {
    //     get => this.AlignerPlcCassetteId;
    // }

    // /// <summary>
    // /// Gets a value indicating whether gets or sets the station number.
    // /// </summary>
    // public override bool CanStart
    // {
    //     get => this.AlignerMeasurementStart; // 对应转到工作角度，可以拍id
    // }

    // /// <summary>
    // /// Gets a value indicating whether the station is processing.
    // /// </summary>
    // public override bool HasWafer
    // {
    //     get => this.HasAlignerWafer; // 对应 alignerInfo.HasAlignerWafer
    // }

    // /// <summary>
    // /// Gets a value indicating whether 边测完成.
    // /// </summary>
    // private volatile bool alignerMeasurementFinished;

    // /// <summary>
    // /// Gets or sets a value indicating whether 边测完成.
    // /// </summary>
    // public override bool MeasurementFinished
    // {
    //     get => this.alignerMeasurementFinished;
    //     set => this.alignerMeasurementFinished = value;
    // }

    // /// <summary>
    // /// Gets a value indicating whether aligner测量完成.
    // /// </summary>
    // public bool AlignerMeasurementFinished { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether Aligner单工位停止.
    /// </summary>
    public bool AlignerStopStatus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Aligner单工位复位.
    /// </summary>
    public bool AlignerResetStatus { get; set; }

    // /// <summary>
    // /// Gets or sets a value indicating whether Aligner单工位启动.
    // /// </summary>
    // public bool AlignerMeasurementStart { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether ALN命令开始.
    /// </summary>
    public bool AlignerCommandStart { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether ALN执行中.
    /// </summary>
    public bool AlignerOngoing { get; set; }

    // /// <summary>
    // /// Gets or sets a value indicating whether ALN Wafer有无.
    // /// </summary>
    // public bool HasAlignerWafer { get; set; }

    // /// <summary>
    // /// Gets or sets a value indicating whether ALN错误.
    // /// </summary>
    // public ushort AlignerError { get; set; }

    /// <summary>
    /// Gets or sets alignerStatus.(暂留).
    /// </summary>
    public ushort AlignerAngle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether ALN任务号.
    /// </summary>
    public ushort AlignerTaskNumber { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Wafer停止角度.
    /// </summary>
    public ushort WaferStopAngle { get; set; }
}
