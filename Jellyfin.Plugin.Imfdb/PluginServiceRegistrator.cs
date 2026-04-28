using Jellyfin.Plugin.Imfdb.Services;
using Jellyfin.Plugin.Imfdb.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Imfdb;

/// <summary>
/// Registers plugin services with Jellyfin.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IImfdbClient, ImfdbClient>();
        serviceCollection.AddSingleton<IImfdbCacheService, ImfdbCacheService>();
        serviceCollection.AddHostedService<FileTransformationRegistrationService>();
    }
}
