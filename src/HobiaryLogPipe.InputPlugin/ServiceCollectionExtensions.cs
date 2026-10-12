using Microsoft.Extensions.DependencyInjection;

namespace HobiaryLogPipe.InputPlugin;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHobiaryLogPipe(
        this IServiceCollection services,
        Action<InputPluginOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<LogPipeWriter>();
        services.AddSingleton<ILogPipeWriter>(sp => sp.GetRequiredService<LogPipeWriter>());
        services.AddHostedService(sp => sp.GetRequiredService<LogPipeWriter>());
        return services;
    }
}
