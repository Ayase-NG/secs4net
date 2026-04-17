using CommunityToolkit.Mvvm.ComponentModel;
namespace GY.EFEM.Hardware.Device.Params;

/// <summary>
/// This class is used to store the information of the robot and the machine.
/// </summary>
public partial class RobotInfo : ObservableObject
{
    /// <summary>
    /// Gets or sets a value indicating whether Robot单工位停止.
    /// </summary>
    [ObservableProperty]
    private ushort status;

    /// <summary>
    /// Gets or sets a value indicating whether Robot单工位停止.
    /// </summary>
    public bool RobotStopStatus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Robot单工位启动.
    /// </summary>
    public bool RobotStartStatus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether robotMoveStatus.
    /// RB执行中.
    /// </summary>
    public bool RobotMoveStatus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether robotResetStatus.
    /// Robot单工位复位.
    /// </summary>
    public bool RobotResetStatus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether putWaferToWorkpiecePlatStatus.
    /// </summary>
    public bool PutWaferToWorkpiecePlatStatus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether getWaferFromWorkpiecePlatStatus.
    /// </summary>
    public bool GetWaferFromWorkpiecePlatStatus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether getWaferFromCassetteStatus.
    /// </summary>
    public bool GetWaferFromCassetteStatus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether putWaferToCassetteStatus.
    /// </summary>
    public bool PutWaferToCassetteStatus { get; set; }

    /// <summary>
    /// Gets or sets robotXCurrentPosition.
    /// </summary>
    public ushort[] RobotXCurrentPosition { get; set; } = new ushort[3];

    /// <summary>
    /// Gets or sets robotXTargetPosition.
    /// </summary>
    public ushort[] RobotXTargetPosition { get; set; } = new ushort[3];

    /// <summary>
    /// Gets or sets robotYCurrentPosition.
    /// </summary>
    public ushort[] RobotYCurrentPosition { get; set; } = new ushort[3];

    /// <summary>
    /// Gets or sets robotYTargetPosition.
    /// </summary>
    public ushort[] RobotYTargetPosition { get; set; } = new ushort[3];

    /// <summary>
    /// Gets or sets robotZCurrentPosition.
    /// </summary>
    public ushort[] RobotZCurrentPosition { get; set; } = new ushort[3];

    /// <summary>
    /// Gets or sets robotZTargetPosition.
    /// </summary>
    public ushort[] RobotZTargetPosition { get; set; } = new ushort[3];

    /// <summary>
    /// Gets or sets robotRCurrentPosition.
    /// </summary>
    public ushort[] RobotRCurrentPosition { get; set; } = new ushort[3];

    /// <summary>
    /// Gets or sets robotRTargetPosition.
    /// </summary>
    public ushort[] RobotRTargetPosition { get; set; } = new ushort[3];

    /// <summary>
    /// Gets or sets afterMeasurementCassetteIndex.
    /// </summary>
    public ushort AfterMeasurementCassetteIndex { get; set; }

    /// <summary>
    /// Gets or sets afterMeasurementSlotId.
    /// </summary>
    public ushort AfterMeasurementSlotId { get; set; }

    /// <summary>
    /// Gets or sets robotSpeed.
    /// </summary>
    public ushort RobotSpeed { get; set; }

    /// <summary>
    /// Gets or sets robotErrCode.
    /// </summary>
    public ushort RobotErrCode { get; set; }
}
