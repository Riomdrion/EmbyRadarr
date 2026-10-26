# EmbyRadarr

Emby Server plugin that shows Radarr's wanted, upcoming and downloading movies inside Emby.

Status: **phase 2** (settings page, connection test, read-only channel) + self-update from GitHub releases.

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

## The Radarr channel

After installing, a **Radarr** channel appears in Emby with three folders. It is read-only: only GET requests go to Radarr.

| Folder | Source | Status text |
|---|---|---|
| Wanted | `GET /api/v3/movie`, monitored and no file | `Wanted` / `Wanted (not released yet)` |
| Upcoming | `GET /api/v3/calendar` (next 90 days) | `Releases 2026-11-14 (digital)` |
| Download queue | `GET /api/v3/queue?includeMovie=true` | `Downloading 45%, 12 min left`, `Paused 10%`, `Importing`, `Failed: ...` |

- Each movie shows the TMDb poster, title, year, genres, runtime and certification. The status is the first line of the description and also the tagline.
- Trailer: Radarr's YouTube id is added as an external link ("YouTube trailer") and as a URL in the description.
- Sorting: default is soonest release first (queue keeps Radarr's order). Emby's sort requests (name, release date, date added, runtime) are honoured if Emby sends them. Each movie also gets a tag (`Wanted`, `Upcoming`, `Downloading`) for filtering.
- Caching: Radarr responses are cached for 1-5 minutes (setting). A background check every 5 minutes notifies Emby when something changed.
- Errors: Radarr offline, wrong key or an empty list show up as a message item in the folder instead of an error.

## Design notes

- The settings page uses the Emby SDK's GenericEdit UI (`IHasUIPages`), as in the SDK's `EmbyPluginUiTemplate`.
  The SDK's "Simple UI" (`BasePluginSimpleUI`) does not support custom buttons, and a classic `IHasWebPages` page would need hand-written HTML/JS plus a custom REST endpoint for the test button.
- Radarr JSON is parsed with `DataContractJsonSerializer` (part of netstandard2.0), so the plugin is a single DLL with no extra dependencies.
