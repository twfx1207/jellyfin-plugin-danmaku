using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Danmaku.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether danmaku is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether playback should be matched automatically.
    /// </summary>
    public bool AutoMatch { get; set; } = true;

    /// <summary>
    /// Gets or sets the default source identifier.
    /// </summary>
    public string DefaultSource { get; set; } = "dandanplay";

    /// <summary>
    /// Gets or sets the dandanplay API base URL.
    /// </summary>
    public string DandanplayBaseUrl { get; set; } = "https://api.dandanplay.net";

    /// <summary>
    /// Gets or sets the dandanplay application identifier.
    /// </summary>
    public string DandanplayAppId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the dandanplay application secret.
    /// </summary>
    public string DandanplayAppSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether related third-party comments should be included.
    /// </summary>
    public bool DandanplayWithRelated { get; set; } = true;

    /// <summary>
    /// Gets or sets the dandanplay simplified/traditional Chinese conversion option.
    /// </summary>
    public int DandanplayChConvert { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the custom HTTP source is enabled.
    /// </summary>
    public bool CustomSourceEnabled { get; set; }

    /// <summary>
    /// Gets or sets the custom source display name.
    /// </summary>
    public string CustomSourceName { get; set; } = "自定义源";

    /// <summary>
    /// Gets or sets the custom source base URL.
    /// </summary>
    public string CustomSourceBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the custom source matching path.
    /// </summary>
    public string CustomMatchPath { get; set; } = "/match";

    /// <summary>
    /// Gets or sets the custom source comments path template.
    /// </summary>
    public string CustomCommentsPath { get; set; } = "/comments/{episodeId}";

    /// <summary>
    /// Gets or sets the optional custom source API key header name.
    /// </summary>
    public string CustomApiKeyHeader { get; set; } = "X-Api-Key";

    /// <summary>
    /// Gets or sets the optional custom source API key.
    /// </summary>
    public string CustomApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the response cache duration in minutes.
    /// </summary>
    public int CacheMinutes { get; set; } = 360;

    /// <summary>
    /// Gets or sets the maximum number of comments returned to a client.
    /// </summary>
    public int MaxComments { get; set; } = 6000;

    /// <summary>
    /// Gets or sets the default overlay opacity.
    /// </summary>
    public double Opacity { get; set; } = 0.9;

    /// <summary>
    /// Gets or sets the default font scale.
    /// </summary>
    public double FontScale { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the time in seconds for a scrolling comment to cross the screen.
    /// </summary>
    public double SpeedSeconds { get; set; } = 8.0;

    /// <summary>
    /// Gets or sets the maximum number of simultaneously displayed comments.
    /// </summary>
    public int MaxOnScreen { get; set; } = 80;
}
