using System;
using System.Linq;
using Jellyfin.Plugin.Tethr.Configuration;

namespace Jellyfin.Plugin.Tethr;

public static class Links
{
    private static readonly object Gate = new();

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    public static UserLink[] All()
    {
        lock (Gate)
        {
            return [.. Config.Links];
        }
    }

    public static UserLink? For(Guid userId)
    {
        lock (Gate)
        {
            return Config.Links.FirstOrDefault(l => l.UserId == userId);
        }
    }

    public static void Save(UserLink link)
    {
        lock (Gate)
        {
            Config.Links = [.. Config.Links.Where(l => l.UserId != link.UserId), link];
            Plugin.Instance?.SaveConfiguration();
        }
    }

    public static void Remove(Guid userId)
    {
        lock (Gate)
        {
            Config.Links = [.. Config.Links.Where(l => l.UserId != userId)];
            Plugin.Instance?.SaveConfiguration();
        }
    }

    public static void Settings(string serverUrl, bool markPlayed)
    {
        lock (Gate)
        {
            Config.ServerUrl = serverUrl.TrimEnd('/');
            Config.MarkPlayed = markPlayed;
            Plugin.Instance?.SaveConfiguration();
        }
    }
}
