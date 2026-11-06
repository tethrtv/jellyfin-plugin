using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Tethr.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string ServerUrl { get; set; } = "https://api.tethr.tv";

    public bool MarkPlayed { get; set; } = true;

    public UserLink[] Links { get; set; } = [];
}

public class UserLink
{
    public Guid UserId { get; set; }

    public string ProfileName { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime? Imported { get; set; }

    public DateTime? HistorySeenUpTo { get; set; }
}
