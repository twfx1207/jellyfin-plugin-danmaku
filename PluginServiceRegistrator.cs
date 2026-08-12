using Jellyfin.Plugin.Danmaku.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Danmaku;

/// <summary>
/// Registers plugin services.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddMemoryCache();
        serviceCollection.AddHttpClient(DanmakuService.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-Danmaku/0.1");
        });
        serviceCollection.AddSingleton<AssociationStore>();
        serviceCollection.AddSingleton<DandanplaySource>();
        serviceCollection.AddSingleton<CustomHttpSource>();
        serviceCollection.AddSingleton(serviceProvider => new DanmakuService(
            serviceProvider.GetRequiredService<MediaBrowser.Controller.Library.ILibraryManager>(),
            serviceProvider.GetRequiredService<AssociationStore>(),
            serviceProvider.GetRequiredService<DandanplaySource>(),
            serviceProvider.GetRequiredService<CustomHttpSource>()));
        serviceCollection.AddHostedService<WebClientInjector>();
    }
}
