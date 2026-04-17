using Microsoft.Extensions.Logging;
using Plc = GY.PLC.Comm.Plc;

namespace GY.EFEM.Hardware.Device
{
    /// <summary>
    /// This class is responsible for controlling the machine by sending commands to the PLC.
    /// </summary>
    public class MachineController
    {
        private readonly PlcController plcController;

        private readonly ILogger<MachineController> logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MachineController"/> class.
        /// Constructor of MachineController.
        /// </summary>
        /// <param name="plcController">plcController.</param>
        /// <param name="logger">logger.</param>
        public MachineController(PlcController plcController, ILogger<MachineController> logger)
        {
            this.plcController = plcController;
            this.logger = logger;
        }

        /// <summary>
        /// Sets the power of the Fan-Feeder Unit (EFU).
        /// </summary>
        /// <param name="eFUPowerId">EFUPowerId.</param>
        /// <param name="eFUPower">EFUPower.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task SetEFUPower(ushort eFUPowerId, ushort eFUPower)
        {
            await this.plcController.WriteHoldings(eFUPowerId, [eFUPower]);
        }

        /// <summary>
        /// Sets the power of the Fan-Feeder Unit (EFU).
        /// </summary>
        /// <param name="eFUPower">EFUPower.</param>
        /// <param name="eFU">EFU.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task SetEFU(ushort eFUPower, ushort eFU)
        {
            await this.plcController.WriteHoldings(eFUPower, [eFU]);
        }

        /// <summary>
        /// SetRecipeFlowActionNo.
        /// </summary>
        /// <param name="actionNoAddress">ActionNoAddress.</param>
        /// <param name="actionNo">ActionNo.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task SetRecipeFlowActionNo(ushort actionNoAddress, ushort actionNo)
        {
            await this.plcController.WriteHoldings(actionNoAddress, [actionNo]);
        }

        /// <summary>
        /// SendOcrResult.
        /// </summary>
        /// <param name="plcOcrAddress">PlcOcrAddress.</param>
        /// <param name="oCR">OCR.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task SendOcrResult(ushort plcOcrAddress, ushort[] oCR)
        {
            await this.plcController.WriteHoldings(plcOcrAddress, oCR);
        }

        /// <summary>
        /// ResetPLCErrorCode.
        /// </summary>
        /// <param name="errorCode">ErrorCode.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task ResetPLCErrorCode(ushort errorCode)
        {
            await this.plcController.WriteHoldings(Plc.Holdings.PlcStatusAddress, [errorCode]);
        }
    }
}
