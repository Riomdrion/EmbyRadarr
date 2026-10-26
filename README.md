# EmbyRadarr

Emby Server plugin that shows Radarr's wanted, upcoming and downloading movies inside Emby.

Status: **phase 1** (skeleton, settings page, connection test).

## Requirements

- Emby Server 4.9.1 or newer (built against `MediaBrowser.Server.Core` 4.9.1.90, the package the official Emby SDK templates use; also compiles against the 4.10.2.0 server assemblies).
- Radarr with API v3 (Radarr 3 and newer).
- To build: .NET SDK 6 or newer (`dotnet --list-sdks`). The output targets `netstandard2.0`, so it runs on every Emby platform.

## Build

```sh
dotnet build src/EmbyRadarr -c Release
# -> src/EmbyRadarr/bin/Release/netstandard2.0/EmbyRadarr.dll
dotnet test          # unit tests (stubbed HTTP, no Emby/Radarr needed)
```

## Install

Copy **only** `EmbyRadarr.dll` into Emby's `plugins` folder, then restart Emby.

| Install type | Plugins folder | Restart | Log file |
|---|---|---|---|
| Linux (deb/rpm) | `/var/lib/emby/plugins/` | `sudo systemctl restart emby-server` | `/var/lib/emby/logs/embyserver.txt` |
| Docker (`emby/embyserver`) | `<your /config volume>/plugins/` | `docker restart <container>` | `<your /config volume>/logs/embyserver.txt` |
| Windows | `%AppData%\Emby-Server\programdata\plugins\` | Dashboard > Restart, or the tray icon | `%AppData%\Emby-Server\programdata\logs\embyserver.txt` |

You can also restart from the Emby dashboard (power icon > Restart) and read logs under Dashboard > Logs.
Plugin log lines are tagged `Radarr`:

```sh
grep -i "radarr" /var/lib/emby/logs/embyserver.txt
```

## Configure

Dashboard > Plugins > Radarr > Settings (also linked in the dashboard menu as "Radarr").

1. Enter the Radarr URL (with URL base if you set one, e.g. `http://host:7878/radarr`) and the API key.
2. Click **Test connection**. On success the quality profile and root folder lists are filled from Radarr.
3. Choose a quality profile and root folder, then **Save**.

Settings are stored in Emby's plugin configuration file (`plugins/configurations/EmbyRadarr.xml`).
The API key is sent to Radarr only in the `X-Api-Key` header. It is never logged and never sent back to the browser (the form shows `********`).

## Design notes

- The settings page uses the Emby SDK's GenericEdit UI (`IHasUIPages`), as in the SDK's `EmbyPluginUiTemplate`.
  The SDK's "Simple UI" (`BasePluginSimpleUI`) does not support custom buttons, and a classic `IHasWebPages` page would need hand-written HTML/JS plus a custom REST endpoint for the test button.
- Radarr JSON is parsed with `DataContractJsonSerializer` (part of netstandard2.0), so the plugin is a single DLL with no extra dependencies.
