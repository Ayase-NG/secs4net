using GY.PLC.Comm;
using Microsoft.EntityFrameworkCore;
using Nacos.AspNetCore.V2;
using SECSGrpcService.Services;
using SECShandler.Handlers;
using SECShandler.Interfaces;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// 读取外部 json 配置文件 secsgrpcsettings.json（可选）用于指定绑定的 Host/Port/UseHttps
builder.Configuration.AddJsonFile("secsgrpcsettings.json", optional: true, reloadOnChange: true);

// 配置文件日志：目录来自配置，文件名格式 YYYY-MM-DD_secs.log
var logDirectory = builder.Configuration.GetValue<string>("Log:Directory")
    ?? Path.Combine(AppContext.BaseDirectory, "logs");
builder.Logging.AddProvider(new DateFileLoggerProvider(logDirectory));

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
// 方法关键节点：同时注册 PlcClient，供 S1F3 读取 PLC Holding 寄存器。
builder.Services.AddNacosAspNet(builder.Configuration, "nacos")
    .AddSingleton<PlcClient>();

var mysqlConnection = builder.Configuration.GetValue<string>("MySql:ConnectionString");
if (!string.IsNullOrWhiteSpace(mysqlConnection))
{
    builder.Services.AddDbContextFactory<TraceabilityDbContext>(options =>
        options.UseMySql(mysqlConnection, ServerVersion.AutoDetect(mysqlConnection)));
}

// 关键分支：统一从配置读取 VID 映射文件路径，支持单路径与候选路径列表。
var vidSection = builder.Configuration.GetSection("VID");
var configuredVidCsv = vidSection.GetValue<string>("CsvPath");
var vidCsv = configuredVidCsv;

if (string.IsNullOrWhiteSpace(vidCsv))
{
    // 关键分支：若未配置单路径，则按配置中的候选路径依次探测首个存在文件。
    var candidateRelativePaths = vidSection.GetSection("CandidatePaths").Get<string[]>() ?? Array.Empty<string>();

    // 关键分支：若配置文件未提供候选项，则保底使用一个默认相对路径。
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

builder.Services.AddSingleton<AlarmStore>();
builder.Services.AddSingleton<ISecsInteractionHistoryStore, RemoteCommandAckHistoryStore>();
builder.Services.AddSingleton<NacosGrpcResolver>();
builder.Services.AddSingleton<SecsGemContext>();
builder.Services.AddSingleton<SecsEfemGrpc>();
builder.Services.AddSingleton<IMeasurementDispatcher, GrpcMeasurementDispatcher>();
builder.Services.AddSingleton<IActiveSxFyDispatcher, ActiveSxFyDispatcher>();

// SECShandler 运行时状态与依赖（统一单例，供多个 handler 共享）
builder.Services.AddSingleton<SecsHandlerRuntimeState>();
builder.Services.AddSingleton<IDevice>(sp => sp.GetRequiredService<SecsHandlerRuntimeState>());
builder.Services.AddSingleton<IReportStorage>(sp => sp.GetRequiredService<SecsHandlerRuntimeState>());
builder.Services.AddSingleton<IEventLinkStorage>(sp => sp.GetRequiredService<SecsHandlerRuntimeState>());
builder.Services.AddSingleton<IEventEnableStorage>(sp => sp.GetRequiredService<SecsHandlerRuntimeState>());

builder.Services.AddSingleton<IPrimaryMessageHandler, CommunicationPrimaryMessageHandler>();
builder.Services.AddSingleton<IPrimaryMessageHandler, EventReportPrimaryMessageHandler>();
builder.Services.AddSingleton<IPrimaryMessageHandler, RemoteCommandPrimaryMessageHandler>();
builder.Services.AddSingleton<IPrimaryMessageHandler, CarrierPrimaryMessageHandler>();

// 注册 SECS PrimaryMessage 持续监听服务（后台服务），与 gRPC 服务并行运行。主要
builder.Services.AddHostedService<SecsPrimaryMessageListenerService>();
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

app.Run();
