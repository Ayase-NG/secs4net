using GY.EFEM.Hardware;
using GY.EFEM.Hardware.Device.Params;
using GY.PLC.Comm;
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(LogLevel.Information)
        .AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
});

var plcLogger = loggerFactory.CreateLogger<PlcClient>();
var controllerLogger = loggerFactory.CreateLogger<PlcController>();

var plcClient = new PlcClient(plcLogger);

var robotInfo = new RobotInfo();
var loadPortInfo = new LoadPortInfo();
var machineInfo = new MachineInfo();
var alignerInfo = new AlignerInfo();
var edgeStationInfo = new EdgeStationInfo();
var surfaceStationInfo = new SurfaceStationInfo();

var plcController = new PlcController(
    controllerLogger,
    robotInfo,
    loadPortInfo,
    machineInfo,
    alignerInfo,
    plcClient,
    edgeStationInfo,
    surfaceStationInfo);

Console.WriteLine("=== PLC 通信测试开始 ===");
Console.WriteLine("默认 PLC 连接参数由 GY.PLC.Comm 内部决定。若需指定 IP/端口，请确认 PlcClient 包的实际配置方式。");
Console.WriteLine();

try
{
    await plcController.FirstRead();

    var systemStart = await plcClient.ReadCoil(Plc.Coils.SystemStart);
    Console.WriteLine($"ReadCoil SystemStart({Plc.Coils.SystemStart}) => success={systemStart.Item1}, message={systemStart.Item2}, value={systemStart.Item3}");

    var robotReset = await plcClient.ReadCoil(Plc.Coils.RobotReset);
    Console.WriteLine($"ReadCoil RobotReset({Plc.Coils.RobotReset}) => success={robotReset.Item1}, message={robotReset.Item2}, value={robotReset.Item3}");

    var robotStatus = await plcClient.ReadHolding(Plc.Holdings.RobotStatus);
    Console.WriteLine($"ReadHolding RobotStatus({Plc.Holdings.RobotStatus}) => success={robotStatus.Item1}, message={robotStatus.Item2}, value={robotStatus.Item3}");

    var alignerAngle = await plcClient.ReadHolding(Plc.Holdings.AlignerAngle);
    Console.WriteLine($"ReadHolding AlignerAngle({Plc.Holdings.AlignerAngle}) => success={alignerAngle.Item1}, message={alignerAngle.Item2}, value={alignerAngle.Item3}");

    Console.WriteLine();
    Console.WriteLine("=== PlcController 缓存结果 ===");
    Console.WriteLine($"machineInfo.StartOnMachine = {machineInfo.StartOnMachine}");
    Console.WriteLine($"robotInfo.Status = {robotInfo.Status}");
    Console.WriteLine($"robotInfo.RobotResetStatus = {robotInfo.RobotResetStatus}");
    Console.WriteLine($"alignerInfo.PlcSlotId = {alignerInfo.PlcSlotId}");
    Console.WriteLine($"edgeStationInfo.PlcSlotId = {edgeStationInfo.PlcSlotId}");
    Console.WriteLine($"surfaceStationInfo.PlcSlotId = {surfaceStationInfo.PlcSlotId}");
    Console.WriteLine($"loadPortInfo.LoadPortStatus = [{string.Join(", ", loadPortInfo.LoadPortStatus)}]");
}
catch (Exception ex)
{
    Console.WriteLine("PLC 通信测试失败:");
    Console.WriteLine(ex);
}

Console.WriteLine();
Console.WriteLine("按任意键退出...");
Console.ReadKey();
