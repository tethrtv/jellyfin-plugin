using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Tethr.Api;

public record DeviceSignIn(string DeviceCode, string UserCode, string VerificationUrl, int ExpiresIn, int Interval);

public record Tokens(string AccessToken, string RefreshToken, long ExpiresIn);

public record Profile(long Id, string Name);

public record Ids(long? Tmdb = null, string? Imdb = null, long? Tvdb = null);

public record ItemRef(string Type, Ids? Ids = null, Ids? Show = null, int? Season = null, int? Number = null);

public record ScrobbleRequest(string Event, double Progress, ItemRef Item, double? DurationSeconds);

public record NewWatch(ItemRef Item, DateTime WatchedAt);

public record NewWatches(IReadOnlyList<NewWatch> Items);

public record Activity(DateTime History);

public record HistoryPage(List<Watch> Items, long Total);

public record Watch(long Id, DateTime WatchedAt, Item Item);

public record Item(string Type, Title Title, Episode? Episode);

public record Title(Ids Ids);

public record Episode(int Season, int Number, Ids Ids);
