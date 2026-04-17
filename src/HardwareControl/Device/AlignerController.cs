using GY.EFEM.Hardware.Device.Params;
using GY.PLC.Comm;
using HardwareControl;
using Microsoft.Extensions.Logging;

namespace GY.EFEM.Hardware.Device;

/// <summary>
/// This class is responsible for controlling the aligner.
/// </summary>
public class AlignerController
{
    private readonly PlcController plcController;

    private readonly ILogger<AlignerController> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlignerController"/> class.
    /// Constructor for AlignerController.
    /// </summary>
    /// <param name="plcController">plcController.</param>
    /// <param name="logger">logger.</param>
    public AlignerController(PlcController plcController, ILogger<AlignerController> logger)
    {
        this.plcController = plcController;
        this.logger = logger;
    }

    /// <summary>
    /// Sets the aligner angle.
    /// </summary>
    /// <param name="alignerAngle">alignerAngle.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task SetAlignerAngle(ushort alignerAngle)
    {
        try
        {
           await this.plcController.WriteHoldings(
               Plc.Holdings.AlignerAngle,
               [alignerAngle]);
        }
        catch
        {
            this.logger.LogInformation("PLC is not connected");
        }
    }

    /// <summary>
    /// Stop the aligner.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task StopAligner()
    {
        try
        {
           await this.plcController.WriteHoldings(
               Plc.Holdings.AlignerTask,
               [(ushort)SystemConstParams.AlignerTask.AlignerStop]);
        }
        catch
        {
            this.logger.LogError("PLC is not connected");
        }
    }
}
