using HardwareControl;

namespace GY.EFEM.Hardware.Device.Params;

/// <summary>
/// This class is used to store the information of the axis.
/// </summary>
public class AxisInfo
{
    /// <summary>
    /// Gets or sets a value indicating whether 当前轴使能.
    /// </summary>
    public bool CurrentAxisEnable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴停止.
    /// </summary>
    public bool CurrentAxisStop { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴复位.
    /// </summary>
    public bool CurrentAxisReset { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴回零.
    /// </summary>
    public bool CurrentAxisReturnZero { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴JOG+.
    /// </summary>
    public bool CurrentAxisJogPositive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴JOG-.
    /// </summary>
    public bool CurrentAxisJogNegative { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴绝对定位.
    /// </summary>
    public bool CurrentAxisAbsPos { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴相对定位正转.
    /// </summary>
    public bool CurrentAxisRelPosForward { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴相对定位反转.
    /// </summary>
    public bool CurrentAxisRelPosReverse { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴使能中.
    /// </summary>
    public bool CurrentAxisEnabling { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴回零中.
    /// </summary>
    public bool CurrentAxisReturningZero { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴回零完成.
    /// </summary>
    public bool CurrentAxisFinishedZero { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴定位中.
    /// </summary>
    public bool CurrentAxisMoving { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴定位完成.
    /// </summary>
    public bool CurrentAxisMovingFinished { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴错误.
    /// </summary>
    public bool CurrentAxisError { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 指定轴ID.
    /// </summary>
    public ushort SelectAxisId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴错误信息.
    /// </summary>
    public ushort CurrentAxisErrorMsg { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴目标位置设定[1]+[2].
    /// </summary>
    public ushort[] SetCurrentAxisPosition { get; set; } = new ushort[2 * SystemConstParams.FoupNumber];

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴目标速度设定[1]+[2].
    /// </summary>
    public ushort[] SetCurrentAxisSpeed { get; set; } = new ushort[2 * SystemConstParams.FoupNumber];

    /// <summary>
    /// Gets or sets a value indicating whether 当前轴当前实时位置[1]+[2].
    /// </summary>
    public ushort[] GetCurrentAxisPosition { get; set; } = new ushort[2 * SystemConstParams.FoupNumber];
}
