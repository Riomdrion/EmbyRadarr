# EmbyRadarr

Emby Server plugin that shows Radarr's wanted, upcoming and downloading movies inside Emby.

Status: settings page, Radarr channel with requests (tap the heart), search, self-update from GitHub releases.

## Requirements

- Emby Server 4.9.1 or newer (built against `MediaBrowser.Server.Core` 4.9.1.90, the package the official Emby SDK templates use; also compiles against the 4.10.0.40 and 4.10.2.0 server assemblies).
- Radarr with API v3 (Radarr 3 and newer).
- To build: .NET SDK 6 or newer (`dotnet --list-sdks`). The output targets `netstandard2.0`, so it runs on every Emby platform.

## Build

```sh
dotnet build src/EmbyRadarr -c Release
# -> src/EmbyRadarr/bin/Release/netstandard2.0/EmbyRadarr.dll
dotnet test          # unit tests (stubbed HTTP, no Emby/Radarr needed)
```

## Install

### First install on a Synology (Emby package), without SSH

Emby's plugins folder is `/volume1/@appdata/EmbyServer/plugins`. It is hidden in File Station and needs the right owner, so use DSM's Task Scheduler once:

1. Download `EmbyRadarr.dll` from the latest GitHub release and put it in a shared folder, e.g. `/volume1/downloads/EmbyRadarr.dll`.
2. DSM > Control Panel > Task Scheduler > Create > Scheduled Task > User-defined script.
   User: `root`. Schedule: run once (any date). Script:

   ```sh
   SRC=/volume1/downloads/EmbyRadarr.dll
   DST=/volume1/@appdata/EmbyServer/plugins
   cp "$SRC" "$DST/EmbyRadarr.dll"
   chown --reference="$DST" "$DST/EmbyRadarr.dll"
   chmod 644 "$DST/EmbyRadarr.dll"
   ```
3. Select the task and click **Run**. Then restart Emby (Dashboard > power icon > Restart).

### Updating (no SSH, no file copying)

Dashboard > Plugins > Radarr > **Check for updates** > **Install** > **Restart Emby now**.
The plugin downloads `EmbyRadarr.dll` from the latest GitHub release, checks its SHA-256, swaps it in (keeping `EmbyRadarr.dll.bak`) and asks Emby to restart.
While the repository is private, enter a fine-grained GitHub token with read-only *Contents* access to this repository in the settings.

### Other platforms

Copy **only** `EmbyRadarr.dll` into Emby's `plugins` folder, then restart Emby.

| Install type | Plugins folder | Restart | Log file |
|---|---|---|---|
| Synology package | `/volume1/@appdata/EmbyServer/plugins/` | Dashboard > Restart | `/volume1/@appdata/EmbyServer/logs/embyserver.txt` (or Dashboard > Logs) |
| Linux (deb/rpm) | `/var/lib/emby/plugins/` | `sudo systemctl restart emby-server` | `/var/lib/emby/logs/embyserver.txt` |
| Docker (`emby/embyserver`) | `<your /config volume>/plugins/` | `docker restart <container>` | `<your /config volume>/logs/embyserver.txt` |
| Windows | `%AppData%\Emby-Server\programdata\plugins\` | Dashboard > Restart, or the tray icon | `%AppData%\Emby-Server\programdata\logs\embyserver.txt` |

Plugin log lines are tagged `Radarr` (Dashboard > Logs > embyserver.txt).

## Releasing a new version

```sh
git tag v0.3.0 && git push origin v0.3.0
```

GitHub Actions runs the tests, builds `EmbyRadarr.dll` with version 0.3.0 and attaches it to a release. Emby then offers it under "Check for updates".

## Configure

Dashboard > Plugins > Radarr > Settings (also linked in the dashboard menu as "Radarr").

1. Enter the Radarr URL (with URL base if you set one, e.g. `http://host:7878/radarr`) and the API key.
2. Click **Test connection**. On success the quality profile and root folder lists are filled from Radarr.
3. Choose a quality profile and root folder, then **Save**.

Settings are stored in Emby's plugin configuration file (`plugins/configurations/EmbyRadarr.xml`).
The API key is sent to Radarr only in the `X-Api-Key` header. It is never logged and never sent back to the browser (the form shows `********`).

## The channel: "Films aanvragen"

Every Emby user sees a channel called **Films aanvragen**, in every Emby app (web, phone, TV). Everything users see is Dutch and never mentions Radarr.

| Folder | What it shows | Example title |
|---|---|---|
| Ontdekken > Nu populair / Populair / Aanbevolen | Radarr's Discover list | `Dune · Tik ♥ om aan te vragen`, `Dune · Aangevraagd`, `Dune · Al te kijken` |
| Zoeken | Results of recent searches (see below) | same as Ontdekken |
| Aangevraagd | Monitored movies without a file | `Dune · Nog niet te kijken` |
| Binnenkort | Radarr calendar, next 90 days | `Dune · Verwacht 14 nov` |
| Wordt binnengehaald | Radarr queue | `Dune · Wordt gedownload 45%, nog 12 min` |

The status is part of every title, so it is always clear that a movie is **not** watchable yet.

### Requesting: tap the heart

Emby channels can't have custom buttons, and channel search is ignored by Emby 4.9+. The one action every Emby app offers on a channel item is **favorite** (the heart):

- Heart on a movie under **Ontdekken** or **Zoeken** that isn't requested yet: it is added to Radarr with the configured quality profile and root folder, and Radarr starts searching.
- Heart on a released movie under **Aangevraagd**: Radarr searches for it again.
- Anything else: nothing happens.

### Searching: a playlist named "Zoek: titel"

A playlist name is the only place in every Emby app where a user can type text the server sees. Creating a playlist named `Zoek: Dune` (also `Zoeken:`, `Search:`):

1. looks the title up via Radarr (`/api/v3/movie/lookup`);
2. shows the results under **Zoeken** as `“Dune” (8 films)`;
3. renames the playlist to `✓ 8 films gevonden voor “Dune”: kijk bij Films aanvragen › Zoeken` (or `✗ ...` when nothing was found);
4. removes that playlist again after about an hour.

The last 15 searches are kept in memory (they are gone after an Emby restart).

### Status banners on posters

The channel's posters get a coloured frame and a banner, in the style of cover-art plugins, so the status is visible at a glance:

| Banner | Colour | When |
|---|---|---|
| AANVRAGEN | grey | Ontdekken / Zoeken, not requested yet |
| AANGEVRAAGD | amber | requested, no file yet |
| VERWACHT 14 NOV | purple | Binnenkort |
| DOWNLOADEN 45% | blue, with progress bar | Wordt binnengehaald |
| AL TE KIJKEN | green | already in the library |
| MISLUKT | red | download failed |

Posters of movies that can't be watched yet are slightly dimmed. This uses `IImageEnhancer` (documented SDK API) and draws with the SkiaSharp library that Emby itself ships (undocumented; compile-only reference, not shipped). If drawing fails, Emby shows the plain poster. The font (Inter Bold, SIL Open Font License, see `Fonts/Inter-LICENSE.txt`) is embedded because a NAS often has no fonts. Can be switched off in the settings.

### Settings

Who may request is a setting (everyone or administrators only). Results are logged (`Radarr` lines in embyserver.txt). Administrators also have a **Search all requested movies now** button. The admin settings page itself stays English.

Other details:
- Posters come from TMDb; trailer links appear as a "YouTube-trailer" external link and in the description.
- Radarr responses are cached for 1-5 minutes (setting). A background check every 5 minutes, and once after every start, tells Emby to reload the channel.
- Errors show up as a message item ("Aanvragen werkt nu even niet") instead of an error.

## Design notes

- The settings page uses the Emby SDK's GenericEdit UI (`IHasUIPages`), as in the SDK's `EmbyPluginUiTemplate`.
  The SDK's "Simple UI" (`BasePluginSimpleUI`) does not support custom buttons, and a classic `IHasWebPages` page would need hand-written HTML/JS plus a custom REST endpoint for the test button.
- Radarr and GitHub are called with .NET's `HttpClient`; JSON is handled with `DataContractJsonSerializer`. Both are part of netstandard2.0, so the plugin is a single DLL with no extra dependencies.
- Pages inside Emby's web app are admin-only (the `/configurationpage` route has `roles:"admin"` and `/UI/View` requires an administrator), so regular users interact through the channel (heart = request, playlist name = search).
