# DAQiFi Desktop

[![Codacy Badge](https://api.codacy.com/project/badge/Grade/17c5dfe7f1fd490f933ca85c14c23e57)](https://app.codacy.com/gh/daqifi/daqifi-desktop?utm_source=github.com&utm_medium=referral&utm_content=daqifi/daqifi-desktop&utm_campaign=Badge_Grade_Settings)

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

Coming soon (Requires .NET6 to build with GitHub Actions)

## Documentation

How data goes from device to the database.

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
