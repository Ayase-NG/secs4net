using System.Collections;
using GY.EFEM.Hardware.Device.Params;
using GY.PLC.Comm;
using HardwareControl;
using Microsoft.Extensions.Logging;

namespace GY.EFEM.Hardware;

/// <summary>
/// PLC控制器.
/// </summary>
public class PlcController
{
    private const int CheckInterval = 100; // 每 100*20ms 线圈状态是否有变化，检测一次

    private const int RefreshInterval = 20; // 每 20ms 刷新一次

    private const int WriteDelay = 50;

    // private const int HeartbeatCheck = 50; // 每秒心跳包检测一次
    private readonly ILogger<PlcController> logger;

    // private readonly ConcurrentQueue<Message> messageQueue = new ();
    private readonly RobotInfo robotInfo;

    private readonly LoadPortInfo loadPortInfo;

    private readonly MachineInfo machineInfo;

    private readonly AlignerInfo alignerInfo;

    private readonly EdgeStationInfo edgeStationInfo;

    private readonly SurfaceStationInfo surfaceStationInfo;

    private readonly PlcClient plcClient;

    /// <summary>
    /// Gets or sets currentCountChanged.
    /// </summary>
    public Action<ushort>? CurrentCountChanged { get; set; }

    // private Task? workTask;

    // private int counter;

    // private BitArray lastBits = new (PlcConstParams.CoilsLength);

    // private CancellationTokenSource? cancellationTokenSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlcController"/> class.
    /// 构造函数.
    /// </summary>
    /// <param name="logger">The logger to log messages.</param>
    /// <param name="robotInfo">The information about the robot.</param>
    /// <param name="loadPortInfo">The information about the load port.</param>
    /// <param name="machineInfo">The information about the machine.</param>
    /// <param name="alignerInfo">The information about the aligner.</param>
    /// <param name="plcClient">PlcClient.</param>
    /// <param name="edgeStationInfo">The information about the edge station.</param>
    /// <param name="surfaceStationInfo">The information about the surface station.</param>
    public PlcController(ILogger<PlcController> logger, RobotInfo robotInfo, LoadPortInfo loadPortInfo, MachineInfo machineInfo, AlignerInfo alignerInfo, PlcClient plcClient, EdgeStationInfo edgeStationInfo, SurfaceStationInfo surfaceStationInfo)
    {
        this.logger = logger;
        this.robotInfo = robotInfo;
        this.loadPortInfo = loadPortInfo;
        this.machineInfo = machineInfo;
        this.alignerInfo = alignerInfo;
        this.plcClient = plcClient;
        this.edgeStationInfo = edgeStationInfo;
        this.surfaceStationInfo = surfaceStationInfo;
        this.plcClient.StartSubZeromqChanged();
        this.plcClient.CoilOnChanged = this.CoilOnChanged;
        this.plcClient.HoldingChanged = this.HoldingChanged;
    }

    /// <summary>
    /// WriteHoldings.
    /// </summary>
    /// <param name="startAddress">0.</param>
    /// <param name="value">1.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task WriteHoldings(ushort startAddress, ushort[] value)
    {
        foreach (ushort t in value)
        {
            await this.plcClient.WriteHolding(startAddress++, t);
        }
    }

    /// <summary>
    /// WriteCoils.
    /// </summary>
    /// <param name="startAddress">0.</param>
    /// <param name="value">1.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task WriteCoils(ushort startAddress, bool[] value)
    {
        foreach (bool t in value)
        {
            await this.plcClient.WriteCoil(startAddress++, t);
        }
    }

    /// <summary>
    /// 启动.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task FirstRead()
    {
        await this.ReadCoils();
        await this.ReadHoldings();
    }

    /// <summary>
    /// emergency stop.
    /// </summary>
    /// <returns>Result<see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task EmergencyStop()
    {
        await this.plcClient.WriteCoil(Plc.Coils.RobotStop, true);
        await this.plcClient.WriteCoil(Plc.Coils.LoadPortStop, true);
        await this.plcClient.WriteCoil(Plc.Coils.AlignerStop, true);
        await this.plcClient.WriteCoil(Plc.Coils.EdgeStop, true);
        await this.plcClient.WriteCoil(Plc.Coils.BSStop, true);
    }

    /// <summary>
    /// 读取线圈.
    /// </summary>
    /// <param name="mode">1.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task SetPlcRunMode(ushort mode)
    {
        await this.plcClient.WriteHolding(Plc.Holdings.SelectRunMode, mode);
    }

    /// <summary>
    /// 读取线圈.
    /// </summary>
    /// <param name="mode">1.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task SetPlcTestMode(ushort mode)
    {
        await this.plcClient.WriteHolding(Plc.Holdings.SelectTestMode, mode);
    }

    private void HoldingChanged(ChangedItem<ushort> item)
    {
        this.logger.LogDebug($"Holding {item.Address} changed to {item.NewValue}");
        switch (item.Address)
        {
            case Plc.Holdings.AfterMeasurementSlotId:
                this.robotInfo.AfterMeasurementSlotId = item.NewValue;

                break;
            case Plc.Holdings.AfterMeasurementCassetteId:
                this.robotInfo.AfterMeasurementCassetteIndex = item.NewValue;

                break;
            case Plc.Holdings.EdgeCurrentTestSlot:
                this.edgeStationInfo.PlcSlotId = item.NewValue;

                break;
            case Plc.Holdings.EdgeCurrentTestCassette:
                this.edgeStationInfo.PlcCassetteId = item.NewValue;

                break;
            case Plc.Holdings.AlignerCurrentTestSlot:
                this.alignerInfo.PlcSlotId = item.NewValue;

                break;
            case Plc.Holdings.AlignerCurrentTestCassette:
                this.alignerInfo.PlcCassetteId = item.NewValue;

                break;
            case Plc.Holdings.SurfaceCurrentTestSlot:
                this.surfaceStationInfo.PlcSlotId = item.NewValue;

                break;
            case Plc.Holdings.SurfaceCurrentTestCassette:
                this.surfaceStationInfo.PlcCassetteId = item.NewValue;

                break;
            case Plc.Holdings.LP1MPResult1:
                this.loadPortInfo.CassetteSlotStatus[0] = item.NewValue;

                break;
            case Plc.Holdings.LP1MPResult2:
                this.loadPortInfo.CassetteSlotStatus[1] = item.NewValue;

                break;
            case Plc.Holdings.LP2MPResult1:
                this.loadPortInfo.CassetteSlotStatus[2] = item.NewValue;

                break;
            case Plc.Holdings.LP2MPResult2:
                this.loadPortInfo.CassetteSlotStatus[3] = item.NewValue;

                break;
            case Plc.Holdings.LP3MPResult1:
                this.loadPortInfo.CassetteSlotStatus[4] = item.NewValue;

                break;
            case Plc.Holdings.LP3MPResult2:
                this.loadPortInfo.CassetteSlotStatus[5] = item.NewValue;

                break;
            case Plc.Holdings.LP4MPResult1:
                this.loadPortInfo.CassetteSlotStatus[6] = item.NewValue;

                break;
            case Plc.Holdings.LP4MPResult2:
                this.loadPortInfo.CassetteSlotStatus[7] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId11:
                this
                    .loadPortInfo.CassetteIdFromPlc[0][0] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId12:
                this
                    .loadPortInfo.CassetteIdFromPlc[0][1] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId13:
                this
                    .loadPortInfo.CassetteIdFromPlc[0][2] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId14:
                this
                    .loadPortInfo.CassetteIdFromPlc[0][3] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId15:
                this
                    .loadPortInfo.CassetteIdFromPlc[0][4] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId16:
                this
                    .loadPortInfo.CassetteIdFromPlc[0][5] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId17:
                this
                    .loadPortInfo.CassetteIdFromPlc[0][6] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId18:
                this
                    .loadPortInfo.CassetteIdFromPlc[0][7] = item.NewValue;

                break;

            case Plc.Holdings.CassetteId21:
                this
                    .loadPortInfo.CassetteIdFromPlc[1][0] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId22:
                this
                    .loadPortInfo.CassetteIdFromPlc[1][1] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId23:
                this
                    .loadPortInfo.CassetteIdFromPlc[1][2] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId24:
                this
                    .loadPortInfo.CassetteIdFromPlc[1][3] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId25:
                this
                    .loadPortInfo.CassetteIdFromPlc[1][4] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId26:
                this
                    .loadPortInfo.CassetteIdFromPlc[1][5] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId27:
                this
                    .loadPortInfo.CassetteIdFromPlc[1][6] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId28:
                this
                    .loadPortInfo.CassetteIdFromPlc[1][7] = item.NewValue;

                break;

            case Plc.Holdings.CassetteId31:
                this
                    .loadPortInfo.CassetteIdFromPlc[2][0] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId32:
                this
                    .loadPortInfo.CassetteIdFromPlc[2][1] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId33:
                this
                    .loadPortInfo.CassetteIdFromPlc[2][2] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId34:
                this
                    .loadPortInfo.CassetteIdFromPlc[2][3] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId35:
                this
                    .loadPortInfo.CassetteIdFromPlc[2][4] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId36:
                this
                    .loadPortInfo.CassetteIdFromPlc[2][5] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId37:
                this
                    .loadPortInfo.CassetteIdFromPlc[2][6] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId38:
                this
                    .loadPortInfo.CassetteIdFromPlc[2][7] = item.NewValue;

                break;

            case Plc.Holdings.CassetteId41:
                this
                    .loadPortInfo.CassetteIdFromPlc[3][0] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId42:
                this
                    .loadPortInfo.CassetteIdFromPlc[3][1] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId43:
                this
                    .loadPortInfo.CassetteIdFromPlc[3][2] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId44:
                this
                    .loadPortInfo.CassetteIdFromPlc[3][3] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId45:
                this
                    .loadPortInfo.CassetteIdFromPlc[3][4] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId46:
                this
                    .loadPortInfo.CassetteIdFromPlc[3][5] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId47:
                this
                    .loadPortInfo.CassetteIdFromPlc[3][6] = item.NewValue;

                break;
            case Plc.Holdings.CassetteId48:
                this
                    .loadPortInfo.CassetteIdFromPlc[3][7] = item.NewValue;

                break;
            case Plc.Holdings.RobotStatus:
                this.robotInfo.Status = item.NewValue;

                break;
            case Plc.Holdings.AlignerStatus:
                this.alignerInfo.Status = item.NewValue;

                break;
            case Plc.Holdings.EdgeStatus:
                this.edgeStationInfo.Status = item.NewValue;

                break;
            case Plc.Holdings.SurfaceStatus:
                this.surfaceStationInfo.Status = item.NewValue;

                break;
            case Plc.Holdings.LoadPort1Status:
                this.loadPortInfo.LoadPort1Status = item.NewValue;

                break;
            case Plc.Holdings.LoadPort2Status:
                this.loadPortInfo.LoadPort2Status = item.NewValue;

                break;
            case Plc.Holdings.LoadPort3Status:
                this.loadPortInfo.LoadPort3Status = item.NewValue;

                break;
            case Plc.Holdings.LoadPort4Status:
                this.loadPortInfo.LoadPort4Status = item.NewValue;

                break;

            case Plc.Holdings.CurrentCassetteChangeCount:
                CurrentCountChanged?.Invoke(item.NewValue);

                break;
        }
    }

    private void CoilOnChanged(ChangedItem<bool> item)
    {
        this.logger.LogDebug($"Coil {item.Address} changed to {item.NewValue}");
        switch (item.Address)
        {
            case Plc.Coils.PlcTestStart:
                this.machineInfo.PlcTestStart = item.NewValue;

                break;
            case Plc.Coils.PlcTestDone:
                this.machineInfo.PlcTestDone = item.NewValue;

                break;
            case Plc.Coils.LoadPort1CloseDoorFinish:
                this.loadPortInfo.LoadPortCloseStatus[0] = item.NewValue;

                break;
            case Plc.Coils.LoadPort2CloseDoorFinish:
                this.loadPortInfo.LoadPortCloseStatus[1] = item.NewValue;

                break;
            case Plc.Coils.LoadPort3CloseDoorFinish:
                this.loadPortInfo.LoadPortCloseStatus[2] = item.NewValue;

                break;
            case Plc.Coils.LoadPort4CloseDoorFinish:
                this.loadPortInfo.LoadPortCloseStatus[3] = item.NewValue;

                break;
            case Plc.Coils.LoadPort1OpenDoorFinish:
                this.loadPortInfo.MappingCassetteStatus[0] = item.NewValue;

                break;
            case Plc.Coils.LoadPort2OpenDoorFinish:
                this.loadPortInfo.MappingCassetteStatus[1] = item.NewValue;

                break;
            case Plc.Coils.LoadPort3OpenDoorFinish:
                this.loadPortInfo.MappingCassetteStatus[2] = item.NewValue;

                break;
            case Plc.Coils.LoadPort4OpenDoorFinish:
                this.loadPortInfo.MappingCassetteStatus[3] = item.NewValue;

                break;
            case Plc.Coils.LoadPort1HasCassette:
                this.loadPortInfo.LoadPortStatus[0] = item.NewValue;

                break;
            case Plc.Coils.LoadPort2HasCassette:
                this.loadPortInfo.LoadPortStatus[1] = item.NewValue;

                break;
            case Plc.Coils.LoadPort3HasCassette:
                this.loadPortInfo.LoadPortStatus[2] = item.NewValue;

                break;
            case Plc.Coils.LoadPort4HasCassette:
                this.loadPortInfo.LoadPortStatus[3] = item.NewValue;

                break;

            // Edge Station Start, finish, has wafer signal
            case Plc.Coils.PlcRequestTakePhotoNotch:
                this.edgeStationInfo.CanStart = item.NewValue;

                break;

            case Plc.Coils.PlcTellPcTakePhotoEdgeFinished:
                this.edgeStationInfo.MeasurementFinished = item.NewValue;

                break;

            case Plc.Coils.EdgeStationHasWafer:
                this.edgeStationInfo.HasWafer = item.NewValue;

                break;

            // Surface Station Start, finish, has wafer signal
            case Plc.Coils.PlcRequestTakePhotoSurface: // 后续换成plc通知pc开始拍照信号
                this.surfaceStationInfo.CanStart = item.NewValue;

                break;
            case Plc.Coils.PlcTellPcTakePhotoForSurfaceFinished:
                this.surfaceStationInfo.MeasurementFinished = item.NewValue;

                break;
            case Plc.Coils.SurfaceStationHasWafer:
                this.surfaceStationInfo.HasWafer = item.NewValue;

                break;

            // Surface Station Start, finish, has wafer signal
            case Plc.Coils.PlcRequestTakePhotoWaferId: // 后续换成plc通知pc开始拍照信号
                this.alignerInfo.CanStart = item.NewValue;

                break;
            case Plc.Coils.PcTellPlcTakePhotoWaferIdFinished:
                this.alignerInfo.MeasurementFinished = item.NewValue;

                break;
            case Plc.Coils.AlignerHasWafer:
                this.alignerInfo.HasWafer = item.NewValue;

                break;

            case Plc.Coils.LoadPort1CommandOngoing:
                this.loadPortInfo.LoadPortOngoing[0] = item.NewValue;
                break;
            case Plc.Coils.LoadPort2CommandOngoing:
                this.loadPortInfo.LoadPortOngoing[1] = item.NewValue;
                break;
            case Plc.Coils.LoadPort3CommandOngoing:
                this.loadPortInfo.LoadPortOngoing[2] = item.NewValue;
                break;
            case Plc.Coils.LoadPort4CommandOngoing:
                this.loadPortInfo.LoadPortOngoing[3] = item.NewValue;
                break;
        }
    }

    /// <summary>
    /// ReadCoils.
    /// </summary>
    private async Task ReadCoils()
    {
        (bool success, string? message, bool? value) = await this.plcClient.ReadCoil(Plc.Coils.RobotCommandOngoing);
        if (success)
        {
            this.robotInfo.RobotMoveStatus = value ?? false;
        }
        else
        {
            this.logger.LogError($"Read Coil {Plc.Coils.RobotCommandOngoing} failed: {message}");
        }

        (success, message, value) = await this.plcClient.ReadCoil(Plc.Coils.RobotReset);
        if (success)
        {
            this.robotInfo.RobotResetStatus = value ?? false;
        }
        else
        {
            this.logger.LogError($"Read Coil {Plc.Coils.RobotReset} failed: {message}");
        }

        (success, message, value) = await this.plcClient.ReadCoil(Plc.Coils.LoadPort1OpenDoorFinish);
        if (success)
        {
            this.loadPortInfo.MappingCassetteStatus[0] = value ?? false;
        }
        else
        {
            this.logger.LogError($"Read Coil {Plc.Coils.LoadPort1OpenDoorFinish} failed: {message}");
        }

        (success, message, value) = await this.plcClient.ReadCoil(Plc.Coils.LoadPort2OpenDoorFinish);
        if (success)
        {
            this.loadPortInfo.MappingCassetteStatus[1] = value ?? false;
        }
        else
        {
            this.logger.LogError($"Read Coil {Plc.Coils.LoadPort2OpenDoorFinish} failed: {message}");
        }

        (success, message, value) = await this.plcClient.ReadCoil(Plc.Coils.LoadPort3OpenDoorFinish);
        if (success)
        {
            this.loadPortInfo.MappingCassetteStatus[2] = value ?? false;
        }
        else
        {
            this.logger.LogError($"Read Coil {Plc.Coils.LoadPort3OpenDoorFinish} failed: {message}");
        }

        (success, message, value) = await this.plcClient.ReadCoil(Plc.Coils.LoadPort4OpenDoorFinish);
        if (success)
        {
            this.loadPortInfo.MappingCassetteStatus[3] = value ?? false;
        }
        else
        {
            this.logger.LogError($"Read Coil {Plc.Coils.LoadPort4OpenDoorFinish} failed: {message}");
        }

        (success, message, value) = await this.plcClient.ReadCoil(Plc.Coils.SystemStart);
        if (success)
        {
            this.machineInfo.StartOnMachine = value ?? false;
        }
        else
        {
            this.logger.LogError($"Read Coil {Plc.Coils.SystemStart} failed: {message}");
        }

        int loadPortStatusLenght = SystemConstParams.FoupNumber;
        ushort readAddress = Plc.Coils.LoadPort1HasCassette;
        for (int i = 0; i < loadPortStatusLenght; i++)
        {
            (success, message, value) = await this.plcClient.ReadCoil(readAddress);

            if (success)
            {
                this.loadPortInfo.LoadPortStatus[i] = value ?? false;
            }
            else
            {
                this.logger.LogError($"Read Coil {Plc.Coils.LoadPort3OpenDoorFinish} failed: {message}");
            }

            readAddress++;
        }

        (success, message, value) = await this.plcClient.ReadCoil(Plc.Coils.PlcRequestTakePhotoNotch);
        if (success)
        {
            this.machineInfo.PlcRequestTakePhotoNotch = value ?? false;
        }
        else
        {
            this.logger.LogError($"Read Coil {Plc.Coils.PlcRequestTakePhotoNotch} failed: {message}");
        }

        (success, message, value) = await this.plcClient.ReadCoil(Plc.Coils.PlcRequestTakePhotoEdge);
        if (success)
        {
            this.machineInfo.PlcRequestTakePhotoEdge = value ?? false;
        }
        else
        {
            this.logger.LogError($"Read Coil {Plc.Coils.PlcRequestTakePhotoEdge} failed: {message}");
        }
    }

    private async Task ReadHoldings()
    {
        for (ushort i = 0; i < SystemConstParams.FoupNumber; i++)
        {
            for (ushort j = 0; j < 8; j++)
            {
                (bool success, string? message, ushort? value) = await this.plcClient.ReadHolding((ushort)(Plc.Holdings.CassetteId11 + (i * 8) + j));
                if (success)
                {
                    this
                        .loadPortInfo.CassetteIdFromPlc[i][j] = value ?? 0;
                }
                else
                {
                    this.logger.LogError($"Read Holding CassetteIdFromPlc Result {i} failed: {message}");
                }
            }
        }

        int foupStatusLength = SystemConstParams.FoupNumber * 2;
        for (ushort i = 0; i < foupStatusLength; i++)
        {
            (bool success, string? message, ushort? value) = await this.plcClient.ReadHolding((ushort)(Plc.Holdings.LP1MPResult1 + i));
            if (success)
            {
                this.loadPortInfo.CassetteSlotStatus[i] = value ?? 0;
            }
            else
            {
                this.logger.LogError($"Read Holding LP MP Result {i} failed: {message}");
            }
        }

        (bool holdingSuccess, string? holdingMessage, ushort? holdingValue) = await this.plcClient.ReadHolding(Plc.Holdings.AlignerCurrentTestSlot);
        if (holdingSuccess)
        {
            this.alignerInfo.PlcSlotId = holdingValue ?? 0;
            this.logger.LogDebug($"Aligner Current Test Slot: {this.alignerInfo.PlcSlotId}");
        }
        else
        {
            this.logger.LogError($"Read Holding {Plc.Holdings.AlignerCurrentTestSlot} failed: {holdingMessage}");
        }

        (holdingSuccess, holdingMessage, holdingValue) = await this.plcClient.ReadHolding(Plc.Holdings.EdgeCurrentTestCassette);
        if (holdingSuccess)
        {
            this.alignerInfo.PlcCassetteId = holdingValue ?? 0;
            this.logger.LogDebug($"Aligner Current Test Cassette: {this.alignerInfo.PlcCassetteId}");
        }
        else
        {
            this.logger.LogError($"Read Holding {Plc.Holdings.AlignerCurrentTestCassette} failed: {holdingMessage}");
        }

        (holdingSuccess, holdingMessage, holdingValue) = await this.plcClient.ReadHolding(Plc.Holdings.EdgeCurrentTestSlot);
        if (holdingSuccess)
        {
            this.edgeStationInfo.PlcSlotId = holdingValue ?? 0;
            this.logger.LogDebug($"Edge Current Test Slot: {this.edgeStationInfo.PlcSlotId}");
        }
        else
        {
            this.logger.LogError($"Read Holding {Plc.Holdings.EdgeCurrentTestSlot} failed: {holdingMessage}");
        }

        (holdingSuccess, holdingMessage, holdingValue) = await this.plcClient.ReadHolding(Plc.Holdings.EdgeCurrentTestCassette);
        if (holdingSuccess)
        {
            this.edgeStationInfo.PlcCassetteId = holdingValue ?? 0;
            this.logger.LogDebug($"Edge Current Test Cassette: {this.edgeStationInfo.PlcCassetteId}");
        }
        else
        {
            this.logger.LogError($"Read Holding {Plc.Holdings.EdgeCurrentTestCassette} failed: {holdingMessage}");
        }

        (holdingSuccess, holdingMessage, holdingValue) = await this.plcClient.ReadHolding(Plc.Holdings.SurfaceCurrentTestSlot);
        if (holdingSuccess)
        {
            this.surfaceStationInfo.PlcSlotId = holdingValue ?? 0;
            this.logger.LogDebug($"Surface Current Test Slot: {this.surfaceStationInfo.PlcSlotId}");
        }
        else
        {
            this.logger.LogError($"Read Holding {Plc.Holdings.SurfaceCurrentTestSlot} failed: {holdingMessage}");
        }

        (holdingSuccess, holdingMessage, holdingValue) = await this.plcClient.ReadHolding(Plc.Holdings.SurfaceCurrentTestCassette);
        if (holdingSuccess)
        {
            this.surfaceStationInfo.PlcCassetteId = holdingValue ?? 0;
            this.logger.LogDebug($"Surface Current Test Cassette: {this.surfaceStationInfo.PlcCassetteId}");
        }
        else
        {
            this.logger.LogError($"Read Holding {Plc.Holdings.SurfaceCurrentTestCassette} failed: {holdingMessage}");
        }

        (holdingSuccess, holdingMessage, holdingValue) = await this.plcClient.ReadHolding(Plc.Holdings.EFU1Switch);
        if (holdingSuccess)
        {
            this.machineInfo.EFU1Power = holdingValue ?? 0;
        }
        else
        {
            this.logger.LogError($"Read Holding {Plc.Holdings.EFU1Switch} failed: {holdingMessage}");
        }

        (holdingSuccess, holdingMessage, holdingValue) = await this.plcClient.ReadHolding(Plc.Holdings.EFU2Switch);
        if (holdingSuccess)
        {
            this.machineInfo.EFU2Power = holdingValue ?? 0;
        }
        else
        {
            this.logger.LogError($"Read Holding {Plc.Holdings.EFU2Switch} failed: {holdingMessage}");
        }

        (holdingSuccess, holdingMessage, holdingValue) = await this.plcClient.ReadHolding(Plc.Holdings.RobotErrorCode);
        if (holdingSuccess)
        {
            this.robotInfo.RobotErrCode = holdingValue ?? 0;
        }
        else
        {
            this.logger.LogError($"Read Holding {Plc.Holdings.RobotErrorCode} failed: {holdingMessage}");
        }

        (holdingSuccess, holdingMessage, holdingValue) = await this.plcClient.ReadHolding(Plc.Holdings.AlnErrorCode);
        if (holdingSuccess)
        {
            this.alignerInfo.StageErrorCode = holdingValue ?? 0;
        }
        else
        {
            this.logger.LogError($"Read Holding {Plc.Holdings.AlnErrorCode} failed: {holdingMessage}");
        }

        (holdingSuccess, holdingMessage, holdingValue) = await this.plcClient.ReadHolding(Plc.Holdings.LpErrorCode);
        if (holdingSuccess)
        {
            this.loadPortInfo.LoadPortErrCode = holdingValue ?? 0;
        }
        else
        {
            this.logger.LogError($"Read Holding {Plc.Holdings.LpErrorCode} failed: {holdingMessage}");
        }

        (holdingSuccess, holdingMessage, holdingValue) = await this.plcClient.ReadHolding(Plc.Holdings.EdgeStageErrorCode);
        if (holdingSuccess)
        {
            this.edgeStationInfo.StageErrorCode = holdingValue ?? 0;
        }
        else
        {
            this.logger.LogError($"Read Holding {Plc.Holdings.EdgeStageErrorCode} failed: {holdingMessage}");
        }

        (holdingSuccess, holdingMessage, holdingValue) = await this.plcClient.ReadHolding(Plc.Holdings.SurfaceStageErrorCode);
        if (holdingSuccess)
        {
            this.surfaceStationInfo.StageErrorCode = holdingValue ?? 0;
        }
        else
        {
            this.logger.LogError($"Read Holding {Plc.Holdings.SurfaceStageErrorCode} failed: {holdingMessage}");
        }

        (holdingSuccess, holdingMessage, holdingValue) = await this.plcClient.ReadHolding(Plc.Holdings.AlignerAngle);
        if (holdingSuccess)
        {
            this.alignerInfo.AlignerAngle = holdingValue ?? 0;
        }
        else
        {
            this.logger.LogError($"Read Holding {Plc.Holdings.AlignerAngle} failed: {holdingMessage}");
        }
    }

    /// <summary>
    /// Convert BitArray to byte[].
    /// </summary>
    /// <param name="bits">bits.</param>
    /// <returns>byte.</returns>
    private byte[] ConvertToBytes(BitArray bits)
    {
        int numBytes = (bits.Length + 7) / 8;
        byte[] bytes = new byte[numBytes];
        bits.CopyTo(bytes, 0);

        return bytes;
    }
}
