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

The project uses GitHub Actions with native .NET tools for continuous integration and deployment:

### Code Quality & Analysis
- **Static Analysis**: Native .NET SDK analyzers with `latest-recommended` analysis level
- **Enhanced Rules**: Roslynator analyzers providing 500+ additional code quality rules
- **Style Enforcement**: Code style rules enforced at build time with `EnforceCodeStyleInBuild`
- **Zero External Dependencies**: No third-party SaaS tools required for code analysis

### Testing & Coverage
- **Automated Testing**: MSTest framework with Moq for mocking
- **Code Coverage**: Native XPlat Code Coverage collector with Cobertura format
- **Coverage Reports**: ReportGenerator produces HTML reports and markdown summaries
- **PR Integration**: Coverage reports automatically posted as pull request comments
- **Coverage Artifacts**: Detailed HTML reports available as downloadable build artifacts
- **80% Minimum**: Maintains project requirement for 80% test coverage

### Build & Deployment
- **Build Pipeline**: Automated build with static analysis on every pull request
- **MSI Installer**: Windows installer builds using Wix Toolset
- **Release Assets**: Automatic installer publishing when GitHub releases are created
- **Dependency Management**: Dependabot handles NuGet and GitHub Actions updates weekly
- **Conventional Commits**: PR title validation ensures consistent commit messages

### Infrastructure
- **Platform**: Windows runners for WPF compatibility
- **Runtime**: .NET 8.0 framework
- **Package Management**: NuGet with version consistency across projects
- **Security**: All analysis and coverage generation runs locally without external data transmission

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