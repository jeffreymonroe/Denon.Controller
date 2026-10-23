# Denon Controller

A web remote for a **Denon AVR-4308CI** receiver, running on a Raspberry Pi. It controls power, volume, mute and input source for the main zone and zones 2 and 3, from any browser on the home network (phone included).

The receiver can be reached over **RS-232** (a USB-to-serial adapter on the Pi) or the network (**telnet**, TCP port 23). The choice is one setting; nothing else changes.

```
 Phone / browser ──► Denon.Web.Remote ──► Denon.Web.Api ──► Denon.Core.Command ──► receiver
   (page + JS)        port 5090            port 5091         serial or TCP
```

## Projects

| Project | What it is |
|---|---|
| `Denon.Web.Api` | ASP.NET Core Web API. Translates REST calls into receiver commands using the device schema in `Schemas/4308ci.json`. |
| `Denon.Web.Remote` | ASP.NET Core MVC web page: volume dials, power and mute switches, source dropdown per zone. Calls the API from the browser. |
| `Denon.Core.Command` (folder `Denon.Core.Telnet`) | Connections to the receiver: `TcpConnection` (network) and `SerialConnection` (RS-232), plus the shared reply reader. |
| `Denon.Core.Model` | Shared models: device schema, configuration, command responses. |
| `Denon.Telnet.Test` | Command-line tool: send commands to the receiver over the network and print the replies. |
| `Denon.Serial.Test` | Command-line tool: send commands to the receiver over the serial port. See its [README](Denon.Serial.Test/README.md). |

`Docs/` holds notes on undocumented commands and the receiver's UPnP service descriptions. The command set comes from Denon's [AVR-4308CI control protocol, version 5.1.0a](https://assets.denon.com/documentmaster/us/avr-4308ciserialprotocol_ver510a.pdf) (PDF on Denon's site; not included here).

## How it behaves

- **One command at a time.** The connection stays open between commands and is reopened automatically if the receiver drops it.
- **Unsolicited status lines are ignored.** The receiver announces changes made from its front panel or remote; those lines are filtered out of command replies.
- **Fails fast when the receiver is off or unplugged.** The API answers `503 Receiver ... not reachable` within about 2 seconds, then immediately for the next 5 seconds, so requests don't pile up.
- **Volume limit.** The API never sets a volume above `Safety:MaxVolume`, and the page's dials stop there.
- **Cross-site protection.** Only the pages listed in `Security:AllowedOrigins` can call the API from a browser.

## Configuration

The committed settings files use example addresses (`192.168.1.100`, `raspberrypi.local`). Put your own in a **`appsettings.{Environment}.Local.json`** file next to them, e.g. `appsettings.Production.Local.json` on the Pi and `appsettings.Development.Local.json` for debugging. Git ignores these files; they are read after `appsettings.{Environment}.json`, and publishing copies them with the app. Only the values that differ are needed:

```json
{
  "DeviceConfig": { "Host": "192.168.1.50" },
  "Security": { "AllowedOrigins": [ "http://my-pi.home.lan:5090", "http://192.168.1.20:5090" ] }
}
```

A list such as `AllowedOrigins` is merged by position, so the local file's list should be at least as long as the one it replaces.

### API — `Denon.Web.Api/appsettings.json`

```jsonc
"DeviceConfig": {
  "ModelFilename": "4308ci.json",       // device schema in Schemas/
  "ConnectionType": "Serial",           // "Serial" or "Telnet"
  "Host": "192.168.x.x",                // receiver address (Telnet)
  "TelnetPort": 23,
  "Serial": {
    "PortName": "/dev/ttyUSB0",         // COMx on Windows; /dev/serial/by-id/... is stable across reboots
    "BaudRate": 9600, "DataBits": 8, "Parity": "None", "StopBits": "One"
  },
  "TimeoutRatio": "1",                  // multiplies every command's reply timeout
  "TelnetConnectionTimeout": 300000     // close an idle network connection after this many ms
},
"Safety": {
  "MaxVolume": 80,                      // 80 = 0 dB reference, 98 = receiver maximum
  "MaxVolumeByZone": { }                // optional per zone, e.g. { "2": 70 }
},
"Security": {
  "AllowedOrigins": [ "http://<pi-name>:5090" ],   // every address the remote page is opened from
  "EnableRawCommandEndpoint": false     // SendCommand bypasses all checks; debugging only
},
"Kestrel": { "Endpoints": { "Http": { "Url": "http://0.0.0.0:5091" } } }
```

### Web remote — `Denon.Web.Remote/appsettings.json`

```jsonc
"DeviceConfig": {
  "RemoteWebApi": "http://<pi-name>:5091/api/Remote/",   // used by the browser, so not "localhost"
  "TimeoutRatio": "1",
  "RemoteWebApiTimeoutSeconds": 5       // optional; server-side call to the API when the page loads
},
"Kestrel": { "Endpoints": { "Http": { "Url": "http://0.0.0.0:5090" } } }
```

### Device schema — `Denon.Web.Api/Schemas/4308ci.json`

Defines the receiver's commands and how to read their replies, plus:

- **`configuration.zones`** — zone IDs (`M`, `2`, `3`) and the names shown on the page.
- **`configuration.sources`** — the input sources:
  ```json
  { "id": "CD", "default": "CD", "name": "Raspotify", "enabled": true }
  ```
  `id` is the receiver's code (sent as `SICD`, `Z2CD`, ...), `name` is an optional custom label (empty = use `default`), and `enabled` controls whether the page offers it. The API always returns every source.

Editing the schema only needs an API restart, not a rebuild.

## API endpoints

All `GET`, under `/api/Remote/`. Swagger UI is at `/swagger` on the API.

| Area | Endpoints |
|---|---|
| Setup | `GetConfiguration` (zones, sources, volume limits, receiver model, API version), `CheckConnectionStatus`, `GetSystemModel` |
| Power | `GetSystemPowerStatus`, `SetSystemPowerOn`, `SetSystemPowerStandBy`, `GetZonePowerStatus`, `SetZonePowerOn`, `SetZonePowerOff` |
| Volume | `GetMasterVolume`, `SetMasterVolume?value=`, `SetMasterVolumeUp/Down`, `SetZoneVolume?zoneId=&value=`, `SetZoneVolumeUp/Down` |
| Mute | `GetMasterVolumeMute`, `SetMasterVolumeMute?value=ON\|OFF`, `...On/Off`, and the `Zone` equivalents with `zoneId` |
| Source | `GetSourceList`, `GetSourceListDefault`, `GetMasterSource`, `SetMasterSource?source=`, `GetZoneSource`, `SetZoneSource` |

Volume is `00`–`98` (half steps as `455` = 45.5 on the main zone) or `UP`/`DOWN`. Invalid values get `400`; an unreachable receiver gets `503`.

## Development

- Open `Denon Controller.sln` in Visual Studio and start **both** `Denon.Web.Api` and `Denon.Web.Remote`.
- `appsettings.Development.json` listens on all interfaces, so the remote can be tried from a phone at `http://<dev-pc-ip>:5000`. Allow ports 5000 and 5091 through the Windows firewall.
- Only one program can use the receiver's connection: stop the services on the Pi (or the test tools) while debugging against the same receiver.

## Deploying to the Raspberry Pi

1. Publish both projects with the **linux-arm64** profile (self-contained; the Pi doesn't need .NET installed).
   - API: `Denon.Web.Api/bin/Release/linux-arm64`
   - Remote: `Denon.Web.Remote/bin/Release/net10.0/linux-arm64`
2. Stop both services on the Pi, copy each publish folder over, and start them again. If copied from Windows: `chmod +x Denon.Web.Api Denon.Web.Remote`.
3. For serial: the account running the API needs the port — `sudo usermod -aG dialout <user>` (then log in again).
4. Check: the API log shows `Receiver connection: serial port ...`, `GET /api/Remote/CheckConnectionStatus` returns `"systemState":"Alive"`, and the page footer shows the receiver model and matching versions.

## Versioning

`Directory.Build.props` sets one version for all projects (currently **1.1.0**); bump it when deploying. The SDK appends the git commit, and the remote's footer shows both services' versions — `AVR-4308USA | Remote 1.1.0+1c9df25 | API 1.1.0+1c9df25` — so a mismatch means one service wasn't redeployed.

## Test tools

```bash
# Network (telnet)
dotnet Denon.Telnet.Test.dll -a <receiver-ip>              # interactive; Up/Down for command history
dotnet Denon.Telnet.Test.dll -a <receiver-ip> -c "MV?" -q  # one command, raw reply only

# Serial
dotnet Denon.Serial.Test.dll -n /dev/ttyUSB0
```

Both keep the last 100 commands in `~/.denon.telnet.test_history` / `~/.denon.serial.test_history`.
