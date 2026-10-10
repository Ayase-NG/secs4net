using GY.PLC.Comm;
using Microsoft.EntityFrameworkCore;
using Nacos.AspNetCore.V2;
using SECSGrpcService.Services;
using SECSGrpcService.Views;
using SECShandler.Handlers;
using SECShandler.Interfaces;
using System.Net;
using System.Threading;
using System.Windows.Forms;

var builder = WebApplication.CreateBuilder(args);

// 读取外部 json 配置文件 secsgrpcsettings.json（可选）用于指定绑定的 Host/Port/UseHttps
builder.Configuration.AddJsonFile("secsgrpcsettings.json", optional: true, reloadOnChange: true);

// 配置文件日志：目录来自配置，文件名格式 YYYY-MM-DD_secs.log
var logDirectory = builder.Configuration.GetValue<string>("Log:Directory")
    ?? Path.Combine(AppContext.BaseDirectory, "logs");
builder.Logging.AddProvider(new DateFileLoggerProvider(logDirectory));
var grpcUiLogProvider = new GrpcUiLogProvider();
// 仅交互式运行时转发 gRPC 日志到窗体；文件日志始终保留。
if (Environment.UserInteractive)
{
    builder.Logging.AddProvider(grpcUiLogProvider);
}

// 从配置中获取端口和绑定地址
var grpcSection = builder.Configuration.GetSection("Grpc");
var grpcHost = grpcSection.GetValue<string>("Host", "0.0.0.0");
var grpcPort = grpcSection.GetValue<int>("Port", 7150);
var grpcUseHttps = grpcSection.GetValue<bool>("UseHttps", true);

// 配置 Kestrel 监听地址/端口
builder.WebHost.ConfigureKestrel(options =>
{
    IPAddress ip;
    if (string.IsNullOrWhiteSpace(grpcHost) || grpcHost == "*" || grpcHost == "+")
        ip = IPAddress.Any;
    else if (!IPAddress.TryParse(grpcHost, out ip))
        ip = IPAddress.Any;

    options.Listen(ip, grpcPort, listenOptions =>
    {
        if (grpcUseHttps)
            listenOptions.UseHttps();
    });
});

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddGrpcReflection();

// Nacos 服务注册（配置来源：secsgrpcsettings.json -> nacos 节点）
// 同时注册 PlcClient，供 S1F3 读取 PLC Holding 寄存器。
builder.Services.AddNacosAspNet(builder.Configuration, "nacos");
//.AddSingleton<PlcClient>();

var mysqlConnection = builder.Configuration.GetValue<string>("MySql:ConnectionString");
if (!string.IsNullOrWhiteSpace(mysqlConnection))
{
    builder.Services.AddDbContextFactory<TraceabilityDbContext>(options =>
        options.UseMySql(mysqlConnection, ServerVersion.AutoDetect(mysqlConnection)));
}

// 统一从配置读取 VID 映射文件路径，支持单路径与候选路径列表。
var vidSection = builder.Configuration.GetSection("VID");
var vidCsv = vidSection.GetValue<string>("CsvPath");

if (string.IsNullOrWhiteSpace(vidCsv))
{
    // 若未配置单路径，则按配置中的候选路径依次探测首个存在文件。
    var candidateRelativePaths = vidSection.GetSection("CandidatePaths").Get<string[]>() ?? Array.Empty<string>();

    // 若配置文件未提供候选项，则保底使用一个默认相对路径。
    if (candidateRelativePaths.Length == 0)
    {
        candidateRelativePaths = ["src/Messages/Config/VID.csv"];
    }

    var candidatePaths = candidateRelativePaths
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(p => Path.IsPathRooted(p)
            ? p
            : Path.Combine(builder.Environment.ContentRootPath, p))
        .ToArray();

    vidCsv = candidatePaths.FirstOrDefault(File.Exists)
        ?? candidatePaths[0];
}

builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<VidMap>>();
    return VidMap.LoadFromCsv(vidCsv!, logger);
});

// 统一从配置读取 CEID 映射文件路径，支持单路径与候选路径列表。
var ceidSection = builder.Configuration.GetSection("CEID");
var configuredCeidCsv = ceidSection.GetValue<string>("CsvPath");
var ceidCsv = configuredCeidCsv;

if (string.IsNullOrWhiteSpace(ceidCsv))
{
    var candidateRelativePaths = ceidSection.GetSection("CandidatePaths").Get<string[]>() ?? Array.Empty<string>();
    if (candidateRelativePaths.Length == 0)
    {
        candidateRelativePaths = ["src/Messages/Config/CEID.csv"];
    }

    var candidatePaths = candidateRelativePaths
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(p => Path.IsPathRooted(p) ? p : Path.Combine(builder.Environment.ContentRootPath, p))
        .ToArray();

    ceidCsv = candidatePaths.FirstOrDefault(File.Exists)
        ?? candidatePaths[0];
}

builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<CeidMap>>();
    return CeidMap.LoadFromCsv(ceidCsv!, logger);
});

// 从运行目录加载报警文本映射；项目构建和发布时会复制 AlarmCode.csv。
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<AlarmCodeMap>>();
    var alarmCodeCsv = Path.Combine(AppContext.BaseDirectory, "AlarmCode.csv");
    return AlarmCodeMap.LoadFromCsv(alarmCodeCsv, logger);
})
    .AddSingleton<AlarmStore>()
    .AddSingleton<ISecsInteractionHistoryStore, RemoteCommandAckHistoryStore>()
    .AddSingleton<IIdempotencyGuard, InMemoryIdempotencyGuard>()
    .AddSingleton<NacosGrpcResolver>()
    .AddSingleton<SecsGemContext>()
    .AddSingleton<SecsEfemGrpc>()
    .AddNacosAspNet(option =>
    {
        option.ServerAddresses = ["http://localhost:8848"];
        option.DefaultTimeOut = 15000;
        option.Namespace = "GWM";
        option.ServiceName = "SECS";
        option.GroupName = "GY";
        option.ClusterName = "DEFAULT";
        option.Weight = 100;
        option.ConfigUseRpc = true;
        option.NamingUseRpc = true;
        option.Ip = grpcHost;
        option.Port = grpcPort;
    })
    // 存储运行过程中的一些数据的接口，后续需优化删减
    .AddSingleton<ICommandDispatcher, GrpcCommandDispatcher>()
    .AddSingleton<IActiveSxFyDispatcher, ActiveSxFyDispatcher>()
    .AddSingleton<SecsHandlerRuntimeState>()
    .AddSingleton<IDevice>(sp => sp.GetRequiredService<SecsHandlerRuntimeState>())
    .AddSingleton<IReportStorage>(sp => sp.GetRequiredService<SecsHandlerRuntimeState>())
    .AddSingleton<IEventLinkStorage>(sp => sp.GetRequiredService<SecsHandlerRuntimeState>())
    .AddSingleton<IEventEnableStorage>(sp => sp.GetRequiredService<SecsHandlerRuntimeState>())
    .AddSingleton<IAlarmEnableStorage>(sp => sp.GetRequiredService<SecsHandlerRuntimeState>())
    .AddSingleton<IAlarmStateStorage>(sp => sp.GetRequiredService<SecsHandlerRuntimeState>())
    .AddSingleton<ITimeSyncStorage>(sp => sp.GetRequiredService<SecsHandlerRuntimeState>())
    .AddSingleton<IS6F11SpoolStorage>(sp => sp.GetRequiredService<SecsHandlerRuntimeState>()) //S6F11上报缓存
    .AddSingleton<IPortContextStorage>(sp => sp.GetRequiredService<SecsHandlerRuntimeState>())
    .AddSingleton<IJobPlanStorage>(sp => sp.GetRequiredService<SecsHandlerRuntimeState>())
    .AddSingleton<IVidValueStorage>(sp => sp.GetRequiredService<SecsHandlerRuntimeState>())
    // 处理各不同功能的SxFy
    .AddSingleton<IPrimaryMessageHandler, CommunicationPrimaryMessageHandler>()
    .AddSingleton<IPrimaryMessageHandler, EventReportPrimaryMessageHandler>()
    .AddSingleton<IPrimaryMessageHandler, RemoteCommandPrimaryMessageHandler>()
    .AddSingleton<IPrimaryMessageHandler, CarrierPrimaryMessageHandler>()
    .AddSingleton<IPrimaryMessageHandler, AlarmPrimaryMessageHandler>()
    .AddSingleton<IPrimaryMessageHandler, TimeSyncPrimaryMessageHandler>()
    .AddSingleton<IPrimaryMessageHandler, RecipePrimaryMessageHandler>()
    .AddSingleton<SecsPrimaryMessageListenerService>()
    .AddHostedService(sp => sp.GetRequiredService<SecsPrimaryMessageListenerService>())
    .AddHostedService<S6F11SpoolReplayService>();
//builder.Services.AddHostedService<DeviceStatusRefreshService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<ReportGrpc>();

if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

Console.WriteLine($"SECSGrpcService 已启动，监听: {(grpcUseHttps ? "https" : "http")}://{grpcHost}:{grpcPort}");
Console.WriteLine("可测试入口: GY.EFEM.ReportGrpcService/ResultReport");
Console.WriteLine("可测试入口: GY.EFEM.ReportGrpcService/ReportAlarm");
Console.WriteLine("如需持续监听SECS PrimaryMessage，请在配置中设置 SecsListener:Enabled=true");

// 只有交互式 Windows 进程显示测试窗体；服务/无人值守模式沿用原有启动方式。
if (Environment.UserInteractive)
{
    // 异步调用说明：先启动 gRPC 宿主及后台监听，再显示共享监听器的测试窗体。
    await app.StartAsync();
    var uiThread = new Thread(() =>
    {
        ApplicationConfiguration.Initialize();
        using var form = new SecsForm(app.Services.GetRequiredService<SecsPrimaryMessageListenerService>(), app.Configuration, grpcUiLogProvider);
        Application.Run(form);
        app.Lifetime.StopApplication();
    });
    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.IsBackground = true;
    uiThread.Start();
    // 异步调用说明：窗体关闭或外部停止宿主时完成后台服务的优雅关闭。
    await app.WaitForShutdownAsync();
}
else
{
    // 异步调用说明：非交互式部署不创建窗口，继续运行原有 gRPC/SECS 服务。
    await app.RunAsync();
}
