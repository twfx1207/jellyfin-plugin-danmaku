using System.Globalization;
using Jellyfin.Plugin.Danmaku.Configuration;
using Jellyfin.Plugin.Danmaku.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Danmaku;

/// <summary>
/// Jellyfin web danmaku plugin.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// The plugin identifier.
    /// </summary>
    public static readonly Guid PluginId = Guid.Parse("2e4ba9dd-7312-4ec7-b1e2-214cbf5b65cc");

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    /// <param name="xmlSerializer">The XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Danmaku";

    /// <inheritdoc />
    public override string Description => "Adds automatic and manual danmaku matching to the Jellyfin web player.";

    /// <inheritdoc />
    public override Guid Id => PluginId;

    /// <summary>
    /// Gets the active plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                DisplayName = "弹幕",
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace)
            }
        ];
    }

    /// <inheritdoc />
    public override void OnUninstalling()
    {
        WebClientInjector.Remove(ApplicationPaths.WebPath);
        base.OnUninstalling();
    }
}
