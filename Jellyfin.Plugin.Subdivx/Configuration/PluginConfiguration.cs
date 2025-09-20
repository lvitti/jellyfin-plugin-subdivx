using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Subdivx.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool UseOriginalTitle { get; set; } = false;
    public bool ShowTitleInResult { get; set; } = true;
    public bool ShowUploaderInResult { get; set; } = true;
    public string Token { get; set; } = string.Empty;
    public string SubXApiUrl { get; set; } = "https://subx-api.duckdns.org";
}