# Architecture

This document is for contributors. It explains how DAQiFi Desktop is structured and how a single sample makes its way from a Nyquist device into the SQLite database. For visual/interaction principles, see [design-philosophy.md](design-philosophy.md). For specific design decisions, see [adr/](adr/).

## System context

How DAQiFi Desktop relates to the world outside it.

```mermaid
flowchart TB
    user(["<b>Operator</b><br/><i>Person</i><br/>Engineer, researcher,<br/>or educator"])

    desktop["<b>DAQiFi Desktop</b><br/><i>System</i><br/>WPF app: discovery,<br/>visualization, logging,<br/>firmware updates"]

    nyquist["<b>Nyquist Device</b><br/><i>External</i><br/>DAQiFi hardware<br/>(Nyquist 1 or 3)"]
    github["<b>GitHub Releases</b><br/><i>External</i><br/>Update check"]
    sentry["<b>Sentry</b><br/><i>External</i><br/>Exception telemetry"]
    firewall["<b>Windows Firewall</b><br/><i>External</i><br/>UDP 30303 rule"]

    user -- "Connects devices,<br/>logs data" --> desktop
    desktop -- "Streaming<br/>(WiFi UDP/TCP,<br/>USB Serial)" --> nyquist
    desktop -- "Firmware<br/>(USB HID)" --> nyquist
    desktop -- "Latest release<br/>(HTTPS)" --> github
    desktop -- "Exceptions<br/>(HTTPS)" --> sentry
    desktop -- "Adds rule<br/>(first run, admin)" --> firewall

    classDef person fill:#08427b,stroke:#073b6f,color:#fff
    classDef system fill:#1168bd,stroke:#0b4884,color:#fff
    classDef external fill:#999,stroke:#6b6b6b,color:#fff
    class user person
    class desktop system
    class nyquist,github,sentry,firewall external
```

## Containers

What lives inside the DAQiFi Desktop process and on the user's machine.

```mermaid
flowchart TB
    user(["<b>Operator</b>"])
    nyquist["<b>Nyquist Device</b><br/><i>External</i>"]

    subgraph app["DAQiFi Desktop (WPF process)"]
        direction TB
        ui["<b>WPF UI</b><br/><i>XAML + MVVM</i><br/>Channels pane,<br/>plot, profiles,<br/>dialogs"]
        domain["<b>Device &amp; Logging Domain</b><br/><i>C# (.NET 10)</i><br/>AbstractStreamingDevice,<br/>Channel, LoggingManager,<br/>DatabaseLogger"]
        core["<b>Daqifi.Core</b><br/><i>NuGet package</i><br/>Transport,<br/>ProtobufProtocolHandler,<br/>discovery, SCPI"]
    end

    sqlite[("<b>SQLite</b><br/><i>EF Core, local file</i><br/>Sessions, samples,<br/>device metadata")]
    config["<b>App.config +<br/>Profiles XML</b><br/><i>CommonApplicationData</i><br/>Settings,<br/>named profiles"]

    user -- "Uses" --> ui
    ui -- "Commands &amp;<br/>observable state" --> domain
    domain -- "SCPI out,<br/>MessageReceived in" --> core
    core -- "Wire protocol" --> nyquist
    domain -- "Bulk inserts" --> sqlite
    ui -- "Reads / writes" --> config

    classDef person fill:#08427b,stroke:#073b6f,color:#fff
    classDef container fill:#438dd5,stroke:#2e6295,color:#fff
    classDef external fill:#999,stroke:#6b6b6b,color:#fff
    class user person
    class ui,domain,core container
    class sqlite container
    class nyquist,config external
    style app stroke:#888,stroke-dasharray:5 5,fill:transparent
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
