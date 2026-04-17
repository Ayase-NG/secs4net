using Microsoft.Extensions.Logging;

namespace GY.EFEM.Hardware.Device;

/// <summary>
/// This class is responsible for controlling the aligner.
/// </summary>
public class AxisController
{
    private readonly PlcController plcController;

    private readonly ILogger<AlignerController> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AxisController"/> class.
    /// Constructor of the LoadPortController class.
    /// </summary>
    /// <param name="plcController">plcController.</param>
    /// <param name="logger">logger.</param>
    public AxisController(PlcController plcController, ILogger<AlignerController> logger)
    {
        this.plcController = plcController;
        this.logger = logger;
    }
}