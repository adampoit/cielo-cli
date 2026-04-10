# cielo

A CLI and monitoring daemon for [Cielo Breez](https://www.cielowigle.com/) smart HVAC controllers.

**cielo** provides device discovery, direct control, continuous monitoring, and rule-based automation for Cielo Breez smart AC controllers. It supports both interactive CLI usage and long-running daemon mode with systemd integration.

## Features

- **Device Management** – Discover and inspect all Cielo devices on your account
- **Direct Control** – Control power, mode, temperature, fan speed, swing, and presets
- **Real-time Streaming** – Watch live WebSocket updates from your devices
- **Continuous Monitoring** – Poll device state with configurable intervals
- **Rule-based Automation** – Trigger custom hooks when temperature/humidity thresholds are crossed
- **Climate Plans** – Apply coordinated settings across multiple devices
- **Outdoor Weather** – Integrate local weather data via Open-Meteo
- **Daemon Mode** – Run as a systemd service with proper integration
- **NixOS Support** – First-class Nix flake and NixOS module

## Table of Contents

- [Prerequisites](#prerequisites)
- [Installation](#installation)
  - [Nix (Recommended)](#nix-recommended)
  - [Standalone Binary](#standalone-binary)
  - [From Source](#from-source)
- [Quick Start](#quick-start)
- [Obtaining Credentials](#obtaining-credentials)
- [Commands](#commands)
  - [Device Discovery](#device-discovery)
  - [Status & Control](#status--control)
  - [Monitoring](#monitoring)
  - [Climate Plans](#climate-plans)
  - [Real-time Streaming](#real-time-streaming)
- [Configuration](#configuration)
- [Monitor Rules](#monitor-rules)
- [Daemon Mode](#daemon-mode)
- [NixOS Module](#nixos-module)
- [Development](#development)
- [License](#license)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (for building from source)
- Valid Cielo Breez account credentials (see [Obtaining Credentials](#obtaining-credentials))

## Installation

### Nix (Recommended)

Install directly from the flake:

```bash
nix profile install github:adampoit/cielo
```

Or run without installing:

```bash
nix run github:adampoit/cielo -- devices
```

### Standalone Binary

Build a self-contained single-file binary:

```bash
dotnet publish src/cielo/cielo.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:GenerateRuntimeConfigurationFiles=true \
  -o ./artifacts/cielo-linux-x64

install -m 755 ./artifacts/cielo-linux-x64/cielo ~/.local/bin/cielo
```

Replace `linux-x64` with your target runtime: `linux-arm64`, `osx-arm64`, `osx-x64`, etc.

### From Source

Run without installing:

```bash
dotnet run --project src/cielo/cielo.csproj -- devices
```

## Quick Start

1. **Initialize configuration:**

```bash
cielo config init
```

2. **Edit `~/.config/cielo/config.json`** with your credentials (see [Obtaining Credentials](#obtaining-credentials))

3. **List your devices:**

```bash
cielo devices
```

4. **Check device status:**

```bash
cielo status --device "Living Room"
```

5. **Control a device:**

```bash
cielo set --device "Living Room" --power on --mode cool --temp 72
```

## Obtaining Credentials

Credentials are extracted from Cielo's web authentication flow:

1. Open https://home.cielowigle.com/ in Chrome
2. Open DevTools with `F12`
3. Go to the **Network** tab
4. Enable **Disable cache** and **Preserve log**
5. Sign in normally and complete any CAPTCHA
6. Click the network entry related to `login`
7. Open its **Response** tab
8. Copy the response JSON somewhere safe
9. Extract the following values to your config file:

```json
{
  "access_token": "...",
  "refresh_token": "...",
  "session_id": "...",
  "user_id": "...",
  "x_api_key": "..."
}
```

The `x_api_key` can be found in request headers to `api.smartcielo.com` if not in the login response.

**Note:** These credentials may stop working if Cielo changes their authentication flow. If refresh fails, repeat the capture process.

## Commands

### Device Discovery

```bash
# List all devices
cielo devices

# List devices as JSON
cielo devices --json
```

### Status & Control

```bash
# Show device status
cielo status --device "Living Room"
cielo status --device "Living Room" --json

# Control a device
cielo set --device "Living Room" --power on --mode cool --temp 72
cielo set --device "Bedroom" --power off
cielo set --device "Office" --mode heat --temp 68 --fan auto --swing auto

# Apply a preset (Breez Max)
cielo set --device "Living Room" --preset "Sleep"

# Debug WebSocket traffic
cielo set --device "Living Room" --power on --debug-wire
```

**Supported options for `set`:**

- `--power` – `on` or `off`
- `--mode` – `auto`, `heat`, `cool`, `dry`, `fan`
- `--temp` – Target temperature (integer)
- `--fan` – `auto`, `low`, `medium`, `high`, `fanspeed`
- `--swing` – `auto`, `auto/stop`, `adjust`, `pos1` through `pos6`
- `--preset` – Preset name (Breez Max only)

### Monitoring

```bash
# Poll device every 30 seconds
cielo monitor --device "Living Room" --interval 30

# Only emit on changes
cielo monitor --device "Living Room" --interval 30 --changes-only

# Include outdoor weather data
cielo monitor --device "Living Room" --interval 60 \
  --weather-lat <LATITUDE> --weather-lon <LONGITUDE>

# Run with rules
cielo monitor --device "Living Room" --rules ~/.config/cielo/rules.json

# Test rules without executing hooks
cielo monitor --device "Living Room" --rules ~/.config/cielo/rules.json --dry-run

# Save history to NDJSON file
cielo monitor --interval 60 --history-file /var/lib/cielo/history.ndjson

# Run as daemon
cielo monitor --daemon --interval 60 --rules ~/.config/cielo/rules.json
```

### Climate Plans

Apply coordinated settings across multiple devices:

```bash
# Apply mode and setpoints via CLI
cielo apply-plan --mode cool \
  --set "Living Room=72" \
  --set "Bedroom=70" \
  --set "Office=74"

# Apply from JSON file
cielo apply-plan --plan /path/to/plan.json

# Apply from stdin
cat plan.json | cielo apply-plan --plan -

# Dry run to validate
cielo apply-plan --mode heat --set "Living Room=68" --dry-run

# Output JSON result
cielo apply-plan --mode cool --set "Living Room=72" --json
```

**Example plan.json:**

```json
{
  "mode": "cool",
  "devices": [
    { "name": "Living Room", "setpoint": 72 },
    { "name": "Bedroom", "setpoint": 70 },
    { "name": "Office", "setpoint": 74 }
  ]
}
```

### Real-time Streaming

```bash
# Stream all device updates
cielo watch

# Stream specific device
cielo watch --device "Living Room"

# Output as JSON
cielo watch --json
```

## Configuration

Default config path: `~/.config/cielo/config.json`

Create a starter template:

```bash
cielo config init
cielo config init --force  # Overwrite existing
```

**Config file structure:**

```json
{
  "access_token": "...",
  "refresh_token": "...",
  "session_id": "...",
  "user_id": "...",
  "x_api_key": "..."
}
```

Use `--config /path/to/config.json` with any command to specify a custom location.

## Monitor Rules

Rules enable threshold-based automation hooks. Rules are defined in JSON and evaluated during monitoring.

Default rules path: `~/.config/cielo/rules.json`

**Example rules.json:**

```json
{
  "version": 1,
  "rules": [
    {
      "name": "living-room-too-cold",
      "device": "Living Room",
      "when": {
        "metric": "roomTemp",
        "below": 64,
        "unit": "F",
        "forSamples": 2,
        "hysteresis": 1
      },
      "cooldownSeconds": 1800,
      "action": {
        "exec": "/home/user/heat-alert.sh",
        "args": ["{{device}}", "{{roomTemp}}", "{{humidity}}"]
      }
    }
  ]
}
```

**Rule options:**

- `name` – Unique rule identifier
- `device` – Device name, MAC address, or appliance ID
- `when.metric` – `roomTemp`, `targetTemp`, or `humidity`
- `when.below` / `when.above` – Threshold value (specify exactly one)
- `when.unit` – Temperature unit (`F` or `C`)
- `when.forSamples` – Consecutive matching polls before firing (default: 1)
- `when.hysteresis` – Re-arm threshold offset (default: 0)
- `cooldownSeconds` – Minimum time between executions
- `action.exec` – Path to executable
- `action.args` – Arguments with template substitution

**Scheduled rules** (time-based activation):

```json
{
  "name": "night-comfort-check",
  "device": "Bedroom",
  "active": {
    "timezone": "America/Los_Angeles",
    "start": "22:00",
    "end": "07:00"
  },
  "when": {
    "metric": "roomTemp",
    "above": 72,
    "unit": "F",
    "forSamples": 2
  },
  "action": {
    "exec": "/home/user/night-alert.sh",
    "args": ["{{device}}", "{{roomTemp}}"]
  }
}
```

**Template variables for `args`:**

| Variable             | Description              |
| -------------------- | ------------------------ |
| `{{rule}}`           | Rule name                |
| `{{device}}`         | Device name              |
| `{{macAddress}}`     | Device MAC address       |
| `{{applianceId}}`    | Appliance ID             |
| `{{roomTemp}}`       | Current room temperature |
| `{{humidity}}`       | Current humidity         |
| `{{targetTemp}}`     | Target temperature       |
| `{{targetTempUnit}}` | Temperature unit         |
| `{{mode}}`           | Current mode             |
| `{{power}}`          | Power state              |
| `{{fan}}`            | Fan speed                |
| `{{ts}}`             | ISO 8601 timestamp       |

Hooks also receive all values as `CIELO_*` environment variables and the full event payload via stdin as JSON.

See [examples/monitor-rules.example.json](examples/monitor-rules.example.json) for a complete example.

## Daemon Mode

The `--daemon` flag runs the monitor inside a .NET generic host with systemd integration:

- Supports systemd notification protocol (`Type=notify`)
- Automatic token refresh
- Graceful shutdown handling

**Example systemd unit:**

```ini
[Unit]
Description=Cielo monitor
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
ExecStart=/usr/local/bin/cielo monitor --daemon --interval 60 \
  --rules /etc/cielo/rules.json \
  --history-file /var/lib/cielo/history.ndjson
Restart=always
RestartSec=5
User=cielo
Group=cielo

[Install]
WantedBy=multi-user.target
```

**Note:** `--daemon` cannot be combined with `--samples`.

## NixOS Module

For NixOS users, a complete module is provided:

```nix
{
  inputs.cielo.url = "github:adampoit/cielo";

  outputs = { nixpkgs, cielo, ... }: {
    nixosConfigurations.myhost = nixpkgs.lib.nixosSystem {
      system = "x86_64-linux";
      modules = [
        cielo.nixosModules.default
        {
          services.cielo-monitor = {
            enable = true;
            configFile = "/run/secrets/cielo-config.json";
            rulesFile = "/etc/cielo/rules.json";
            interval = 60;
            changesOnly = true;

            weather = {
              latitude = 0.0;
              longitude = 0.0;
            };
          };
        }
      ];
    };
  };
}
```

**Module options:**

- `enable` – Enable the service
- `configFile` – Path to auth config (required)
- `rulesFile` – Path to rules JSON
- `interval` – Polling interval in seconds (default: 60)
- `device` – Monitor specific device only
- `changesOnly` – Only emit on changes
- `dryRun` – Test rules without executing
- `weather.latitude` / `weather.longitude` – Open-Meteo coordinates
- `weather.refreshMinutes` – Weather refresh interval (default: 15)

After changing NuGet dependencies, regenerate the lock file:

```bash
nix run .#fetch-deps -- ./nix/nuget-deps.json
```

## Development

**Build:**

```bash
dotnet build cielo.slnx
```

**Run tests:**

```bash
dotnet test cielo.slnx
```

**Nix development shell:**

```bash
nix develop
```

**Format Nix files:**

```bash
nix fmt
```

## License

MIT License – see [LICENSE](LICENSE) for details.

---

**Disclaimer:** This is an unofficial tool and is not affiliated with Cielo WiGle Inc. Use at your own risk.
