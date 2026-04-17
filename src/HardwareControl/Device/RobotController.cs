using GY.PLC.Comm;
using Microsoft.Extensions.Logging;

namespace GY.EFEM.Hardware.Device;

/// <summary>
/// This class is responsible for controlling the robot.
/// </summary>
public class RobotController
{
    private readonly PlcController plcController;

    private readonly ILogger<RobotController> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RobotController"/> class.
    /// Constructor of the class.
    /// </summary>
    /// <param name="plcController">plcController.</param>
    /// <param name="logger">logger.</param>
    public RobotController(PlcController plcController, ILogger<RobotController> logger)
    {
        this.plcController = plcController;
        this.logger = logger;
    }

    /// <summary>
    /// This method is used to stop the robot.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task RobotStop()
    {
        await this.plcController.WriteCoils(Plc.Coils.SystemStop, [true]);
    }

    /// <summary>
    /// This method is used to reset the robot.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task RobotReset()
    {
        await this.plcController.WriteCoils(Plc.Coils.RobotReset, [true]);
    }

    /// <summary>
    /// This method is used to enable the robot.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task RobotEnable()
    {
        await this.plcController.WriteCoils(Plc.Coils.RobotEnable, [true]);
    }

    /// <summary>
    /// This method is used to set the measurement mode of the robot.
    /// </summary>
    /// <param name="mode">mode.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task SetMeasurementMode(ushort mode)
    {
        // manual: true, auto: false
        await this.plcController.WriteHoldings(Plc.Holdings.SelectRunMode, [mode]);
    }

    /*/// <summary>
    /// This method is used to set the result classification mode of the robot.
    /// </summary>
    /// <param name="mode">mode.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task SetResultClassificationMode(ushort mode)
    {
        try
        {
            await this.plcController.WriteHoldings(Plc.Holdings.ResultClassificationMode, [mode]);
        }
        catch
        {
            this.logger.LogError("PLC is not connected");
        }
    }*/

    /// <summary>
    /// This method is used to set the robot speed.
    /// </summary>
    /// <param name="speed">speed.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task SetRobotSpeed(ushort speed)
    {
        try
        {
            await this.plcController.WriteHoldings(Plc.Holdings.CurrentAxisSpeed1, [speed]);
        }
        catch
        {
            this.logger.LogError("PLC is not connected");
        }
    }

    /*
    public void TellPlcTakePhotoStatus(bool status)
    {
        try
        {
            this.plcController.WriteCoil(ConstParams.TellPlcTakePhotoStatus,
            [
                status
            ]);
        }
        catch
        {
            this.logger.LogInformation("PLC is not connected");
        }
    }
    public void ResetAskTakePhotos()
    {
        try
        {
            this.plcController.WriteCoil(ConstParams.AskTakePhotoMessage,
            [
                false
            ]);
        }
        catch
        {
            this.logger.LogInformation("PLC is not connected");
        }
    }*/
}
