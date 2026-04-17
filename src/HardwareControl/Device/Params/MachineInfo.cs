namespace GY.EFEM.Hardware.Device.Params;

/// <summary>
/// This class is used to store the information of the machine.
/// </summary>
public class MachineInfo
{
    /// <summary>
    /// Gets or sets a value indicating whether machineStatus 机器启动.
    /// </summary>
    public bool StartOnMachine { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 系统停止.
    /// </summary>
    public bool StopMachine { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 系统初始化.
    /// </summary>
    public bool InitialMachine { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 系统心跳.
    /// </summary>
    public bool MachineHeartbeat { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether STAGE单工位停止.
    /// </summary>
    public bool StageStop { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether STAGE单工位复位.
    /// </summary>
    public bool StageReset { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether STAGE单工位启动.
    /// </summary>
    public bool StageStart { get; set; }

    /// <summary>
    /// Gets or sets EFU1Power.
    /// </summary>
    public ushort EFU1Power { get; set; }

    /// <summary>
    /// Gets or sets EFU2Power.
    /// </summary>
    public ushort EFU2Power { get; set; }

    /// <summary>
    /// Gets or sets pLCErrMsg.
    /// </summary>
    public ushort PLCErrMsg { get; set; }

    /// <summary>
    /// Gets or sets cassetteIndexForCodeReader.
    /// </summary>
    public ushort CassetteIndexForCodeReader { get; set; }

    /// <summary>
    /// Gets or sets recipeFlowActionNo.
    /// </summary>
    public ushort RecipeFlowActionNo { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether cassetteMeasurementDone.
    /// </summary>
    public bool CassetteMeasurementDone { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether plcRequestTakePhotoNotch.
    /// </summary>
    public bool PlcRequestTakePhotoNotch { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether plcRequestTakePhotoEdge.
    /// </summary>
    public bool PlcRequestTakePhotoEdge { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether plcRequestTakePhotoCenter.
    /// </summary>
    public bool PlcTestStart { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether plcRequestTakePhotoCenter.
    /// </summary>
    public bool PlcTestDone { get; set; }
}
