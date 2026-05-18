# Architecture

This document is for contributors. It explains how DAQiFi Desktop is structured and how a single sample makes its way from a Nyquist device into the SQLite database. For visual/interaction principles, see [design-philosophy.md](design-philosophy.md). For specific design decisions, see [adr/](adr/).

## System context

How DAQiFi Desktop relates to the world outside it.

```mermaid
C4Context
    title System Context — DAQiFi Desktop

    Person(user, "Operator", "Engineer, researcher, or educator collecting data")

    System(desktop, "DAQiFi Desktop", "WPF app: discovery, live visualization, session logging, firmware updates")

    System_Ext(nyquist, "Nyquist Device", "DAQiFi hardware (Nyquist 1 or 3) over WiFi and/or USB")
    System_Ext(github, "GitHub Releases API", "Source of update notifications")
    System_Ext(sentry, "Sentry", "Unhandled-exception telemetry")
    System_Ext(firewall, "Windows Firewall", "Host firewall — UDP 30303 rule for device discovery")

    Rel(user, desktop, "Connects devices, configures channels, starts logging")
    Rel(desktop, nyquist, "UDP discovery (30303), TCP streaming, USB-Serial, USB HID for firmware")
    Rel(desktop, github, "Checks latest release", "HTTPS")
    Rel(desktop, sentry, "Reports unhandled exceptions", "HTTPS")
    Rel(desktop, firewall, "Creates inbound UDP 30303 rule on first run (requires admin)")
```

## Containers

What lives inside the DAQiFi Desktop process and on the user's machine.

```mermaid
C4Container
    title Container View — DAQiFi Desktop

    Person(user, "Operator")
    System_Ext(nyquist, "Nyquist Device")

    Container_Boundary(app, "DAQiFi Desktop (WPF process)") {
        Container(ui, "WPF UI", "XAML + ViewModels (CommunityToolkit.Mvvm)", "Channels pane, plot, profiles, dialogs")
        Container(domain, "Device & Logging Domain", "C# (.NET 10)", "AbstractStreamingDevice, Channel, LoggingManager, DatabaseLogger")
        Container(core, "Daqifi.Core", "NuGet package", "Transport (TCP/Serial/HID), ProtobufProtocolHandler, discovery, SCPI")
    }

    ContainerDb(sqlite, "SQLite", "Local file (EF Core)", "Sessions, samples, device metadata")
    Container_Ext(config, "App.config + Profiles XML", "Files in CommonApplicationData", "Settings, named profiles")

    Rel(user, ui, "Uses")
    Rel(ui, domain, "Commands and observable state")
    Rel(domain, core, "Sends SCPI, subscribes to MessageReceived")
    Rel(core, nyquist, "Wire protocol")
    Rel(domain, sqlite, "Bulk inserts via EF Core")
    Rel(ui, config, "Reads/writes")
```

## Streaming data flow

One sample's journey from the device wire to the database. Every step below was verified against the current code; the layer that owns each step is in parentheses.

```mermaid
sequenceDiagram
    participant HW as Nyquist Device
    participant Core as Daqifi.Core<br/>(DaqifiDevice + ProtobufProtocolHandler)
    participant Dev as AbstractStreamingDevice<br/>(WiFi or Serial subclass)
    participant Ch as AbstractChannel
    participant LM as LoggingManager
    participant DBL as DatabaseLogger
    participant DB as SQLite

    HW->>Core: Bytes (TCP or USB-Serial)
    Core->>Core: Decode protobuf → DaqifiOutMessage
    Core->>Dev: MessageReceived event
    Dev->>Dev: HandleInboundMessage → ProtobufProtocolHandler.HandleAsync
    Dev->>Dev: OnStreamMessageReceived(DaqifiOutMessage)
    Note over Dev: For each active analog channel:<br/>scale raw ADC (WiFi) or<br/>use pre-scaled float (USB)
    Dev->>Ch: channel.ActiveSample = new DataSample(...)
    Ch->>Ch: Apply NCalc scale expression (if set)
    Ch->>LM: OnChannelUpdated event
    LM->>DBL: logger.Log(sample)   // iterates all registered ILoggers
    DBL->>DBL: _buffer.Add(sample)  // BlockingCollection<DataSample>
    Note over DBL: Background Consumer thread<br/>polls every 100 ms
    DBL->>DB: context.BulkInsert(samples) in EF Core transaction
```

### Notes on the flow

- **WiFi vs USB scaling.** WiFi firmware sends raw ADC counts; USB firmware sends pre-scaled floats already in volts. `OnStreamMessageReceived` branches on this — see `AnalogInData` vs `AnalogInDataFloat` in [AbstractStreamingDevice.cs](../Daqifi.Desktop/Device/AbstractStreamingDevice.cs).
- **Two parallel paths per protobuf message.** The same `DaqifiOutMessage` produces per-channel `DataSample`s (the path above) *and* a single `DeviceMessage` carrying device-level state (battery, status, target frequency). The latter is dispatched via `LoggingManager.HandleDeviceMessage`.
- **Loggers are a list, not a single class.** `LoggingManager.Loggers` is an `ILogger` collection. `DatabaseLogger` is the persistent one, but `PlotLogger` and `SummaryLogger` also subscribe.
- **Backpressure.** `DatabaseLogger` uses a `BlockingCollection<DataSample>` producer/consumer split so the UI/device threads never wait on disk. The consumer drains in ~100 ms ticks and bulk-inserts via `EFCore.BulkExtensions.Sqlite`.
- **Timestamps.** `Daqifi.Core.TimestampProcessor` handles hardware-counter rollover and provides the firmware-measured inter-message delta (immune to TCP jitter).

## Key entry points

| Concern | Start here |
|---|---|
| New device transport | `Daqifi.Desktop/Device/AbstractStreamingDevice.cs` and the subclasses under `WiFiDevice/`, `SerialDevice/` |
| Channel behavior or scaling | `Daqifi.Desktop/Channel/AbstractChannel.cs` (`ActiveSample` setter) |
| Logging pipeline | `Daqifi.Desktop/Loggers/LoggingManager.cs` and `DatabaseLogger.cs` |
| Plot rendering and downsampling | See [adr/001-viewport-aware-downsampling.md](adr/001-viewport-aware-downsampling.md) |
| Visual and interaction design | See [design-philosophy.md](design-philosophy.md) |
