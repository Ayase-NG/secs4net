using Microsoft.Extensions.Logging;

namespace GY.EFEM.Hardware.Device;

/// <summary>
/// This class is responsible for controlling the SuctionPad.
/// </summary>
public class SuctionPadController
{
    private readonly PlcController plcController;
    private readonly ILogger<AlignerController> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SuctionPadController"/> class.
    /// Constructor for AlignerController.
    /// </summary>
    /// <param name="plcController">plcController.</param>
    /// <param name="logger">logger.</param>
    public SuctionPadController(PlcController plcController, ILogger<AlignerController> logger)
    {
        this.plcController = plcController;
        this.logger = logger;
    }
}