using SECSGrpcService.Services;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// 读取外部 json 配置文件 secsgrpcsettings.json（可选）用于指定绑定的 Host/Port/UseHttps
builder.Configuration.AddJsonFile("secsgrpcsettings.json", optional: true, reloadOnChange: true);

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

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<SecsEfemGrpc>();

if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
