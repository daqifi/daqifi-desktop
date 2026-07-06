# DAQiFi Desktop

> "Revolutionizing the data collection experience with convenient, portable device connectivity."
>
> The official Windows desktop application for DAQiFi hardware — real-time visualization, session logging, and firmware updates, all in one place.

[![Latest release](https://img.shields.io/github/v/release/daqifi/daqifi-desktop?style=flat-square&label=release&color=brightgreen&cacheSeconds=3600)](https://github.com/daqifi/daqifi-desktop/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-blue?style=flat-square)](https://github.com/daqifi/daqifi-desktop/releases)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple?style=flat-square)](https://dotnet.microsoft.com/)

[daqifi.com](https://daqifi.com) · [daqifi-core SDK](https://github.com/daqifi/daqifi-core) · [Issues](https://github.com/daqifi/daqifi-desktop/issues)

---

## What is DAQiFi Desktop?

DAQiFi hardware is designed to get out of the way so you can focus on the data, not the collection process. DAQiFi Desktop is the application that makes that possible — connect a Nyquist device over WiFi or USB, configure your channels, start a logging session, and watch your data arrive in real time. No custom scripting required.

If you are building automated pipelines or integrating DAQiFi devices into your own software, the [daqifi-core](https://github.com/daqifi/daqifi-core) .NET SDK gives you programmatic access to the same hardware.

## Quick install / first run

1. Install the [.NET 10.0 Desktop Runtime for Windows](https://dotnet.microsoft.com/download/dotnet/10.0) if you don't already have it.
2. Download the latest `DAQifiDesktop_Setup.msi` from the [Releases page](https://github.com/daqifi/daqifi-desktop/releases).
3. Run the installer.
4. Launch **DAQiFi Desktop**.
5. Click **Connect** and let the app discover your Nyquist device on the local network, or enter its IP address manually.
6. Enable the channels you want to log and press **Start Logging**.

## Common applications

- **Space research** — sensor data acquisition for moon regolith testing
- **Medical R&D** — prosthetic socket pressure monitoring
- **Industrial monitoring** — continuous analog and digital I/O logging
- **Engineering education** — hands-on data acquisition with SCPI-compliant hardware

## Where DAQiFi Desktop fits

| Layer | Repo | What it does |
|---|---|---|
| Hardware | Nyquist 1 / Nyquist 3 | Wireless DAQ devices (WiFi + USB, battery-powered) |
| SDK | [daqifi-core](https://github.com/daqifi/daqifi-core) | .NET library for device communication and data streaming |
| **App** | **daqifi-desktop** | **GUI for device connection, visualization, logging, and firmware updates** |
| User code | Your project | Custom dashboards, test rigs, or automated pipelines built on daqifi-core |

## What you can do

| Capability | What it means for you |
|---|---|
| Auto-discovery over WiFi (UDP, port 30303) | Devices appear automatically when they are on the same network |
| Manual IP connection | Connect directly to a known device address when broadcast discovery is not available |
| USB / Serial connection | Use USB as an alternative to WiFi — same data, no network configuration needed |
| Multiple simultaneous devices | Connect and log from more than one Nyquist at the same time |
| Real-time channel visualization | Analog and digital channels plotted live with a viewport-aware minimap for large datasets |
| Start / stop logging sessions | Record data to a local SQLite database; sessions are preserved between runs |
| Per-channel formula scaling | Apply a custom NCalc expression (e.g. `x * 0.001 + 273.15`) to convert raw values before display and logging |
| Digital output & PWM | Drive digital pins high/low, or run PWM on capable pins (per-channel duty cycle, device-wide frequency) straight from the Channels pane |
| CSV export with optional averaging | Export one or more sessions to `.csv`; optionally downsample by averaging N consecutive samples |
| Named profiles | Save and restore device connections, active channels, and sampling rate — switch between setups without reconfiguring |
| Firmware update via USB HID | Update Nyquist firmware from within the app — no separate tool needed |

## Supported devices

| Device | Analog inputs | Resolution | Input range | Digital I/O | Transport | Power |
|---|---|---|---|---|---|---|
| Nyquist 1 | 16 | 12-bit | 0 – 5 V | Yes | 802.11n WiFi + USB | Battery |
| Nyquist 3 | 8 | 18-bit | ±10 V | Yes | 802.11n WiFi + USB | Battery |

Both devices are SCPI-compliant and compatible with LabVIEW.

## WiFi connectivity

**Requirements:**

- Computer and DAQiFi device must be on the same subnet
- UDP port 30303 must be reachable (configured automatically when the app runs with administrator privileges)
- Virtual machines: use bridged networking, not NAT

**Troubleshooting:**

1. Run DAQiFi Desktop as administrator so it can configure the firewall rule automatically.
2. Confirm the computer and device are on the same WiFi network.
3. Test reachability with `ping <device-ip>`.
4. If discovery does not find the device, use **Manual Connection** and enter the IP address directly.

**Port reference:**

| Protocol | Port | Purpose |
|---|---|---|
| UDP | 30303 | Device discovery broadcasts |
| TCP | Device-specific (typically 9760) | Data streaming |

## Requirements

- **OS**: Windows 10 or later (x64)
- **Runtime**: [.NET 10.0 Desktop Runtime for Windows](https://dotnet.microsoft.com/download/dotnet/10.0) (install separately before running the MSI)
- **Privileges**: Administrator rights recommended for automatic firewall configuration during initial setup

## Community & support

- **Bug reports and feature requests**: [GitHub Issues](https://github.com/daqifi/daqifi-desktop/issues)
- **Commercial inquiries and custom hardware**: [daqifi.com](https://daqifi.com)

## Build from source

Running the app requires **Windows** (the UI targets WPF / `net10.0-windows`). Building requires the
[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — the exact SDK version is pinned in
[`global.json`](global.json).

```bash
git clone https://github.com/daqifi/daqifi-desktop.git
cd daqifi-desktop
dotnet build                          # restore + build the solution
dotnet run --project Daqifi.Desktop   # launch the app
```

Run the unit tests (no hardware required):

```bash
dotnet test --filter "TestCategory!=Ui&FullyQualifiedName!~WindowsFirewallWrapperTests"
```

On macOS/Linux the solution compiles (the Windows-targeted projects set `EnableWindowsTargeting`),
but the app and the Windows-only test projects cannot run there, so solution-wide `dotnet test`
fails. The cross-platform `net10.0` library projects (Common, DataModel, IO) build and test on any
OS — run their test projects individually, e.g.
`dotnet test Daqifi.Desktop.IO.Test/Daqifi.Desktop.IO.Test.csproj`. See the "Cross-Platform
Development (macOS)" section in [CLAUDE.md](CLAUDE.md) for details.

The `DAQifiDesktop_Setup.msi` installer is built from the `Daqifi.Desktop.Setup` project (WiX Toolset)
and is normally produced by CI on release. The WiX project harvests the app's **published** output, so
build it locally by publishing first, then building the setup project (mirroring CI):

```bash
dotnet publish Daqifi.Desktop/Daqifi.Desktop.csproj -c Release
dotnet build -c Release Daqifi.Desktop.Setup
```

The UI-automation test gate drives the real GUI against an attached device — see
[Daqifi.Desktop.UITest/README.md](Daqifi.Desktop.UITest/README.md).

## Contributing

Please read the [Contributing Guidelines](CONTRIBUTING.md) before opening a pull request. See [docs/architecture.md](docs/architecture.md) for the streaming data pipeline and system overview, and [docs/design-philosophy.md](docs/design-philosophy.md) for UI/UX principles. All PRs require a conventional commit title (`feat:`, `fix:`, `docs:`, `deps:`, `chore:`) and at least one approving review from a DAQiFi core member.

## For maintainers

To cut a release: push a `<major>.<minor>.<patch>` tag (e.g. `git tag 3.3.0 && git push origin 3.3.0`). CI builds the MSI and creates a **draft** GitHub Release with the installer attached. Review and edit the auto-generated notes in the draft, then click **Publish release** — assets must be attached before publishing because the repo uses GitHub's Immutable Releases feature. The app version is set in `Daqifi.Desktop/Daqifi.Desktop.csproj` (`<Version>`). Follow [semantic versioning](https://semver.org/); breaking changes should use the `feat!:` prefix in the PR title.

Unhandled exceptions are reported to Sentry. The DSN lives in
[`Daqifi.Desktop/App.config`](Daqifi.Desktop/App.config); like any Sentry client DSN it is write-only
— it can submit crash events but cannot read them back — so it is safe to commit and ship in the app,
and is not a leaked secret.

---

<div align="center">

Built by [DAQiFi](https://daqifi.com) · Licensed under [MIT](LICENSE)

</div>
