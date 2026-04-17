using CommunityToolkit.Mvvm.ComponentModel;

namespace GY.EFEM.Hardware.Device.Params
{
    /// <summary>
    /// This class represents the information of an edge station.
    /// </summary>
    [ObservableObject]
    public partial class EdgeStationInfo : StationExternalInfo
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
        //     get => this.EdgePlcSlotId;
        // }
        //
        // /// <summary>
        // /// Gets 当前检测的Plc上slotId.
        // /// </summary>
        // public override ushort PlcCassetteId
        // {
        //     get => this.EdgePlcCassetteId;
        // }
        //
        // /// <summary>
        // /// Gets a value indicating whether gets or sets the station number.
        // /// </summary>
        // public override bool CanStart
        // {
        //     get => this.EdgeMeasurementStart; // 对应 Edge可以开始检测， Notch位置
        // }
        //
        // /// <summary>
        // /// Gets a value indicating whether the station is processing.
        // /// </summary>
        // public override bool HasWafer
        // {
        //     get => this.HasEdgeWafer;
        // }
        //
        // /// <summary>
        // /// Gets or sets a value indicating whether ALN命令开始.
        // /// </summary>
        // public bool EdgeMeasurementStart { get; set; }

        // /// <summary>
        // /// Gets or sets stageErrorCode.
        // /// </summary>
        // public ushort StageErrorCode { get; set; }

        // /// <summary>
        // /// Gets or sets a value indicating whether ALN Wafer有无.
        // /// </summary>
        // public bool HasEdgeWafer { get; set; }
        //
        // /// <summary>
        // /// Gets a value indicating whether 边测完成.
        // /// </summary>
        // private volatile bool edgeMeasurementFinished;

        // /// <summary>
        // /// Gets or sets a value indicating whether 边测完成.
        // /// </summary>
        // public override bool MeasurementFinished
        // {
        //     get => this.edgeMeasurementFinished;
        //     set => this.edgeMeasurementFinished = value;
        // }

        // /// <summary>
        // /// Gets or sets a value indicating whether plc当前检测的cassetteId.
        // /// </summary>
        // public ushort EdgePlcCassetteId { get; set; }
        //
        // /// <summary>
        // /// Gets or sets a value indicating whether plc当前检测的slotId.
        // /// </summary>
        // public ushort EdgePlcSlotId { get; set; }
    }
}
