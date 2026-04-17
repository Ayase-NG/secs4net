using GY.PLC.Comm;
using Microsoft.Extensions.Logging;

namespace GY.EFEM.Hardware.Device;

/// <summary>
/// This class is responsible for controlling the load port of the EFEM.
/// </summary>
public class LoadPortController
{
    private readonly PlcController plcController;

    private readonly ILogger<LoadPortController> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoadPortController"/> class.
    /// Constructor of the LoadPortController class.
    /// </summary>
    /// <param name="plcController">plcController.</param>
    /// <param name="logger">logger.</param>
    public LoadPortController(ILogger<LoadPortController> logger, PlcController plcController)
    {
        this.logger = logger;
        this.plcController = plcController;
    }

    /// <summary>
    /// Sets the selected slots id to the PLC.
    /// </summary>
    /// <param name="slotsId">slotsId.</param>
    /// <returns>R <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task SetSelectedSlotsIdToPlc(ushort[] slotsId)
    {
        await this.plcController.WriteHoldings(Plc.Holdings.LoadPort1SelectedSlot1, slotsId);
    }
}
