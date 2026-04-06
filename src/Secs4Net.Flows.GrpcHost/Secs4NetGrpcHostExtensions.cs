using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secs4Net;
using System.Diagnostics.CodeAnalysis;

namespace Secs4Net.Flows.GrpcHost;

/// <summary>
/// 注册 HSMS + <see cref="SecsGem"/>，与 DeviceWorkerService 中扩展方法等价，供独立 Host 使用。
/// </summary>
public static class Secs4NetGrpcHostExtensions
{
    public static IServiceCollection AddSecs4NetForGrpcHost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TLogger>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TLogger : class, ISecsGemLogger
    {
        services.Configure<SecsGemOptions>(configuration.GetSection("secs4net"));
        services.AddSingleton<ISecsConnection, HsmsConnection>();
        services.AddSingleton<ISecsGem, SecsGem>();
        services.AddSingleton<ISecsGemLogger, TLogger>();
        return services;
    }
}
