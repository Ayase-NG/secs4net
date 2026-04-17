namespace GY.EFEM.Hardware.Device.Params
{
    /// <summary>
    /// 工位外部信息接口.
    /// </summary>
    public abstract class StationExternalInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StationExternalInfo"/> class.
        /// </summary>
        public StationExternalInfo()
        {
        }

        // /// <summary>
        // /// Gets station Id.
        // /// </summary>
        // public int Id { get; init; } = 1;

        /// <summary>
        /// Gets or sets station Name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current slot id.
        /// </summary>
        public ushort PlcSlotId
        {
            get => this.plcSlotId;
            set => this.plcSlotId = value;
        } // 对应 this.alignerInfo.PlcSlotId

        /// <summary>
        /// Gets or sets the current cassette id.
        /// </summary>
        public ushort PlcCassetteId
        {
            get => this.plcCassetteId;
            set => this.plcCassetteId = value;
        } // 对应 this.alignerInfo.PlcCassetteId

        /// <summary>
        /// Gets or sets a value indicating whether it gets or sets the current slot.
        /// </summary>
        public bool CanStart
        {
            get => this.canStart;
            set => this.canStart = value;
        } // 对应 this.alignerInfo.Start

        /// <summary>
        /// Gets or sets a value indicating whether it gets or sets the current slot.
        /// </summary>
        public bool MeasurementFinished
        {
            get => this.measurementFinished;
            set => this.measurementFinished = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether it gets or sets the current slot.
        /// </summary>
        public bool HasWafer
        {
            get => this.hasWafer;
            set => this.hasWafer = value;
        } // 对应 this.alignerInfo.HasAlignerWafer

        /// <summary>
        /// Gets or sets stageErrorCode.
        /// </summary>
        public ushort StageErrorCode { get; set; }

        private volatile ushort plcSlotId;

        private volatile ushort plcCassetteId;

        private volatile bool hasWafer;

        private volatile bool canStart;

        private volatile bool measurementFinished;
    }
}
