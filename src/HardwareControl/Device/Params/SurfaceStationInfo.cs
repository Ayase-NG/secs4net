using CommunityToolkit.Mvvm.ComponentModel;

namespace GY.EFEM.Hardware.Device.Params
{
    /// <summary>
    /// Represents the surface station information.
    /// </summary>
    [ObservableObject]
    public partial class SurfaceStationInfo : StationExternalInfo
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
        //     get => this.SurfacePlcSlotId;
        // }

        // /// <summary>
        // /// Gets 当前检测的Plc上slotId.
        // /// </summary>
        // public override ushort PlcCassetteId
        // {
        //     get => this.SurfacePlcCassetteId;
        // }

        // /// <summary>
        // /// Gets a value indicating whether gets or sets the station number.
        // /// </summary>
        // public override bool CanStart
        // {
        //     get => this.SurfaceMeasurementStart; // 对应 alignerInfo.Start
        // }

        // /// <summary>
        // /// Gets a value indicating whether the station is processing.
        // /// </summary>
        // public override bool HasWafer
        // {
        //     get => this.HasSurfaceWafer; // 对应 alignerInfo.HasAlignerWafer
        // }

        // /// <summary>
        // /// Gets or sets stageErrorCode.
        // /// </summary>
        // public ushort StageErrorCode { get; set; }

        // /// <summary>
        // /// Gets a value indicating whether 边测完成.
        // /// </summary>
        // private volatile bool surfaceMeasurementFinished;
        //
        // /// <summary>
        // /// Gets or sets a value indicating whether 边测完成.
        // /// </summary>
        // public override bool MeasurementFinished
        // {
        //     get => this.surfaceMeasurementFinished;
        //     set => this.surfaceMeasurementFinished = value;
        // }

        // /// <summary>
        // /// Gets or sets a value indicating whether ALN命令开始.
        // /// </summary>
        // public bool SurfaceMeasurementStart { get; set; }

        // /// <summary>
        // /// Gets or sets a value indicating whether ALN Wafer有无.
        // /// </summary>
        // public bool HasSurfaceWafer { get; set; }

        // /// <summary>
        // /// Gets a value indicating whether the surface measurement is finished.
        // /// </summary>
        // public bool SurfaceMeasurementFinished { get; private set; }

        // /// <summary>
        // /// Gets or sets a value indicating whether plc当前检测的cassetteId.
        // /// </summary>
        // public ushort SurfacePlcCassetteId { get; set; }
        //
        // /// <summary>
        // /// Gets or sets a value indicating whether plc当前检测的slotId.
        // /// </summary>
        // public ushort SurfacePlcSlotId { get; set; }
    }
}
