# DAQiFi Desktop

Windows desktop application (.NET) that is used to communicate with DAQiFi hardware.

## Tech Stack

- .NET 8.0
- WPF
- SQLite

## Dependencies

- EntityFramework (ORM)
- Google Protocol Buffers (read incoming data from DAQiFi hardware)
- MahApps (UI components)
- Oxyplot (for graphing)

## CI/CD

The project uses GitHub Actions for continuous integration and deployment:

- **Build & Test**: Automated build, testing, and code coverage on every pull request
- **Code Analysis**: Native .NET analyzers and Roslynator for code quality
- **Code Coverage**: ReportGenerator produces coverage reports as build artifacts
- **MSI Installer**: Automated Windows installer builds using Wix Toolset
- **Release**: Automatic release asset publishing when GitHub releases are created
- **Dependency Updates**: Dependabot manages NuGet and GitHub Actions dependencies weekly

All workflows run on .NET 8.0 with Windows runners for compatibility with WPF.

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