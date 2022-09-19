# DAQifi Desktop

Windows desktop application (.NET) that is used to communicate with DAQifi hardware.

## Tech Stack

- .NET 4.8
- WPF
- SqlServerSe

## Dependencies

- EntityFramework (ORM)
- Google Protocol Buffers (communication with DAQifi hardware)
- MahApps (UI components)
- Oxyplot (for graphing)

## CI/CD

Coming soon (Requires .NET6 to build with GitHub Actions)

## Documentation

How data goes from device to the database.

```mermaid
sequenceDiagram
DAQifiHardware->>IStreamingDevice: Protobuf Message
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
