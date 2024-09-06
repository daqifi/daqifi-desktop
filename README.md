# DAQiFi Desktop

[![Codacy Badge](https://app.codacy.com/project/badge/Grade/9819d20e349c465a8d6c73e57d0bbf76)](https://www.codacy.com/gh/daqifi/daqifi-desktop/dashboard?utm_source=github.com&amp;utm_medium=referral&amp;utm_content=daqifi/daqifi-desktop&amp;utm_campaign=Badge_Grade)

Windows desktop application (.NET) that is used to communicate with DAQiFi hardware.

## Tech Stack

- .NET 4.8
- WPF
- SqlServerSe

## Dependencies

- EntityFramework (ORM)
- Google Protocol Buffers (read incoming data from DAQiFi hardware)
- MahApps (UI components)
- Oxyplot (for graphing)

## CI/CD

None yet as this Requires `.NET6` to build with GitHub Actions.

## Observability

Exceptions are sent to [BugSnag](https://app.bugsnag.com/daqifi/daqifi-desktop/)

## Documentation

How data goes from the device to the database.

```mermaid
sequenceDiagram
DAQiFiHardware->>IStreamingDevice: Protobuf Message
IStreamingDevice->>MessageConsumer: Protobuf Message
MessageConsumer->>MessageConsumer: Decode Message
MessageConsumer->>IDevice:OnMessageReceived()
IDevice->>IChannel:Set Active Sample
IChannel->>IChannel:Scale Sample(Expression)
IChannel->>LoggingManager:OnChannelUpdated()
LoggingManager->>DatabaseLogger:HandleChannelUpdate()
DatabaseLogger->>DatabaseLogger:Add to Buffer
DatabaseLogger->>DatabaseLogger:ConsumerThread
DatabaseLogger->>Database:Bulk Insert Buffer
```

## Installer

- Uses [Wix Toolset](https://wixtoolset.org/)
- Separate solution `Daqifi.Desktop.Setup`

## Contribution

Please read [Contributing Guidelines](CONTRIBUTING.md) before contributing.