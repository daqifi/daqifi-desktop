# DAQiFi Desktop

Windows desktop application (.NET) that is used to communicate with DAQiFi hardware.

## Tech Stack

- .NET 9.0
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
- **Static Analysis**: .NET SDK analyzers and Roslynator for code quality enforcement
- **Code Coverage**: ReportGenerator creates HTML reports and posts summaries to PRs (80% minimum required)
- **MSI Installer**: Automated Windows installer builds using Wix Toolset
- **Release**: Automatic release asset publishing when GitHub releases are created
- **Dependency Updates**: Dependabot manages NuGet and GitHub Actions dependencies weekly

All workflows run on .NET 9.0 with Windows runners for WPF compatibility.

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

## WiFi Device Connectivity

DAQiFi Desktop discovers and connects to DAQiFi devices over WiFi using UDP broadcasts and TCP connections.

### Network Requirements
- **Same Network**: Computer and DAQiFi device must be on the same network/subnet
- **Firewall**: UDP port 30303 must be allowed (configured automatically with admin privileges)
- **Virtual Machines**: Use bridged networking mode for VM environments

### Troubleshooting WiFi Discovery
1. **Run as Administrator** - Required for automatic firewall configuration
2. **Check Network Connection** - Ensure computer and device are on same WiFi network
3. **Verify Connectivity** - Test with `ping <device-ip>` from command prompt
4. **Manual Connection** - Use manual IP connection if discovery fails

### Port Configuration
- **UDP Discovery**: Port 30303 (device discovery broadcasts)
- **TCP Data**: Device-specific port (varies by device, typically 9760)

## Contribution

Please read [Contributing Guidelines](CONTRIBUTING.md) before contributing.
