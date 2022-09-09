# DAQiFi Desktop

Windows desktop application (.NET) that is used to communicate with DAQiFi hardware.

## Tech Stack

- .NET 4.5
- WPF

## Dependencies

- EntityFramework (ORM)
- Google Protocol Buffers (read incoming data from DAQiFi hardware)
- MahApps (UI components)
- Oxyplot (for graphing)

## CI/CD

Coming soon

## Documentation

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
