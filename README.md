# Tethr for Jellyfin

Logs what everyone on your Jellyfin server watches to [Tethr](https://tethr.tv), whichever app they watch in.

- Plays are logged as they happen, including pauses, so you can carry on in another app.
- When someone connects, what they've already watched here is sent once. Anything Tethr already has is skipped.
- Every 15 minutes, things they've watched elsewhere are marked played here. Marking something unplayed here never removes it from Tethr.

## Installing

In Jellyfin, open Dashboard, then Plugins, then Repositories, and add:

```
https://tethrtv.github.io/jellyfin-plugin/manifest.json
```

Then find Tethr in the Catalogue, install it and restart Jellyfin. Updates show up there too.

## Setting up

Open Dashboard, then Plugins, then Tethr. Next to each person, choose Connect, go to tethr.tv/activate, enter the code and pick their profile. People you don't connect aren't logged.

If you run your own Tethr, put its address in the settings. Nothing needs registering, because every Tethr server knows this plugin.

## Building

You need the .NET 9 SDK.

```sh
dotnet build -c Release
```

To try a build, copy `Jellyfin.Plugin.Tethr/bin/Release/net9.0/Jellyfin.Plugin.Tethr.dll` into a `Tethr` folder in your server's plugins folder and restart Jellyfin. It works with Jellyfin 10.11.

Pushing a tag like `v0.2.0` builds a release and adds it to the plugin repository. Update the changelog in `build.yaml` first.

## Licence

MIT
