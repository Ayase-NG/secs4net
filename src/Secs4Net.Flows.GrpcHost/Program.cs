using Microsoft.AspNetCore.Server.Kestrel.Core;
using Secs4Net.Flows;
using Secs4Net.Flows.GrpcHost;
using Secs4Net.Flows.Sgrs;

// gRPC 需要 HTTP/2。开发环境请使用 https:// 端口（见 launchSettings）或自行配置 HTTP/2 + 证书。
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(static o =>
{
    o.ConfigureEndpointDefaults(static lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
});

builder.Services.AddSecs4NetForGrpcHost<GrpcHostSecsGemLogger>(builder.Configuration);

// 通用调度器 + SGRS 客制化 Profile（若切换 Fab，改为注册其他 IFabFlowProfile 实现即可）。
builder.Services.AddSingleton<IFabFlowProfile, SgrsFabFlowProfile>();
builder.Services.AddSingleton<SecsFlowDispatcher>();

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<FlowBridgeGrpcService>();
app.MapGet("/", static () =>
    "Secs4Net Flow gRPC Host：请使用 gRPC 客户端连接 HTTPS 地址调用 FlowBridge.SendPrimary；HSMS 目标在 appsettings 的 secs4net 节。\n");

app.Run();
