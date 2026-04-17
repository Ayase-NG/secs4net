namespace GY.EFEM.Hardware.Device.Params;

/// <summary>
/// This class is used to store the information of the suction pad.
/// </summary>
public class SuctionPadInfo
{
    /// <summary>
    /// Gets or sets a value indicating whether 吸真空.
    /// </summary>
    public bool SuctionPadCapture { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 破真空.
    /// </summary>
    public bool SuctionPadRelease { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether 真空反馈.
    /// </summary>
    public bool SuctionPadResult { get; set; }
}