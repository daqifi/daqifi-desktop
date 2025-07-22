# Claude Development Assistant Context

This document provides context for Claude to assist with the DAQiFi Desktop application development.

> **Note**: Please check the `.cursor/rules` folder periodically for any updates to development standards and incorporate those changes into this document to maintain consistency across AI-assisted development tools.

## Project Overview

DAQiFi Desktop is a Windows desktop application for communicating with DAQiFi hardware products. Our goal is to modernize data acquisition with user-friendly and intuitive products.

### Technology Stack
- .NET 8.0 and WPF for the desktop application
- SQLite for local data storage
- Google Protocol Buffers for device communication
- EntityFramework for database operations
- NLog for logging framework
- MSTest with Moq for testing

## Architecture

The application follows MVVM (Model-View-ViewModel) pattern with these projects:
- **Daqifi.Desktop** - Main project containing UI
- **Daqifi.Desktop.Bootloader** - Bootloader/firmware domain
- **Daqifi.Desktop.Common** - Shared components
- **Daqifi.Desktop.DataModel** - Data models
- **Daqifi.Desktop.IO** - Device messaging
- **Daqifi.Desktop.Setup** - Wix installer

## Coding Standards

### Naming Conventions
- **Interfaces**: PascalCase with I prefix (e.g., `IStreamingDevice`)
- **Classes**: PascalCase (e.g., `FirewallConfiguration`)
- **Methods**: PascalCase (e.g., `InitializeFirewallRules`)
- **Properties**: PascalCase (e.g., `StreamingFrequency`)
- **Private Fields**: camelCase with _ prefix (e.g., `_firewallHelper`)
- **Constants**: SCREAMING_SNAKE_CASE (e.g., `RULE_NAME`)

### Code Style
- **Indentation**: 4 spaces
- **Max line length**: 120 characters
- **Braces**: Allman style (opening brace on new line)
- **Regions**: Use #region for logical grouping
- **File organization**: One main class per file

### Example Code Style
```csharp
public class FirewallConfiguration : IFirewallConfiguration
{
    #region Constants
    private const string RULE_NAME = "DAQiFi_TCP_Rule";
    #endregion

    #region Private Fields
    private readonly IFirewallHelper _firewallHelper;
    #endregion

    #region Constructor
    public FirewallConfiguration(IFirewallHelper firewallHelper)
    {
        _firewallHelper = firewallHelper;
    }
    #endregion

    #region Public Methods
    public void InitializeFirewallRules()
    {
        // Implementation
    }
    #endregion
}
```

## Key Commands

### Build and Test
```bash
# Build the solution
dotnet build

# Run all tests
dotnet test

# Run with code coverage (80% minimum required)
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover

# Run specific test project
dotnet test Daqifi.Desktop.Tests/Daqifi.Desktop.Tests.csproj
```

### Code Quality
```bash
# Format code
dotnet format

# Run code analysis
dotnet build /p:EnforceCodeStyleInBuild=true
```

## Testing Standards

- Test projects must have `.Test` suffix
- Test files named `*Tests.cs`
- **80% minimum code coverage**
- Use MSTest framework with Moq for mocking
- Follow Arrange-Act-Assert pattern
- Mock external dependencies

### Test Example
```csharp
[TestClass]
public class FirewallConfigurationTests
{
    private Mock<IFirewallHelper> _mockFirewallHelper;
    private FirewallConfiguration _configuration;

    [TestInitialize]
    public void Setup()
    {
        _mockFirewallHelper = new Mock<IFirewallHelper>();
        _configuration = new FirewallConfiguration(_mockFirewallHelper.Object);
    }

    [TestMethod]
    public void InitializeFirewallRules_Should_CreateRules()
    {
        // Arrange
        _mockFirewallHelper.Setup(x => x.CreateRule(It.IsAny<string>())).Returns(true);

        // Act
        _configuration.InitializeFirewallRules();

        // Assert
        _mockFirewallHelper.Verify(x => x.CreateRule(RULE_NAME), Times.Once);
    }
}
```

## Security Guidelines

- **Admin privileges**: Required for firewall changes
- **Passwords**: Use SecureString
- **Input validation**: Always validate user input
- **Error handling**: Secure error messages, no sensitive data in logs
- **Connection strings**: Store securely, never hardcode

## Logging Standards

- Use NLog framework
- Log levels: Error, Warning, Info, Debug
- Logs stored in: `%CommonApplicationData%\DAQifi\Logs`
- Include stack traces for errors
- No sensitive data in logs

### Logging Example
```csharp
private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

try
{
    // Operation
}
catch (Exception ex)
{
    Logger.Error(ex, "Failed to initialize firewall rules");
    throw;
}
```

## Device Communication

The application communicates with DAQiFi devices using:
- UDP for device discovery
- TCP for data streaming and commands
- Protocol Buffers for message serialization

Key classes:
- `DaqifiDeviceFinder.cs` - Handles device discovery
- `WiFiDevice` - Manages WiFi device connections
- Protocol buffer messages defined in `.proto` files

## Database

SQLite database managed through Entity Framework:
- Connection string in app settings
- Migrations handled automatically
- Local data storage for device configurations and logged data
- Use async methods for database operations

## Documentation Standards

- **README.md**: Keep updated with setup instructions
- **XML comments**: Required on all public APIs
- **Conventional commits**: Use feat:, fix:, docs:, chore:, deps:
- **Keep documentation in sync with code changes**

### XML Comment Example
```csharp
/// <summary>
/// Initializes firewall rules for DAQiFi device communication.
/// </summary>
/// <exception cref="UnauthorizedAccessException">Thrown when admin privileges are not available.</exception>
public void InitializeFirewallRules()
{
    // Implementation
}
```

## Git Workflow

- Main branch: `main`
- Feature branches: `feature/description`
- Fix branches: `fix/description`
- Chore branches: `chore/description`
- Use conventional commits
- Squash merge to main
- All PRs require code review

## Common Tasks

When working on:
- **Device connectivity**: Check `/Device/` directory
- **UI changes**: Update both View and ViewModel following MVVM
- **Data persistence**: Use Entity Framework patterns
- **Protocol changes**: Update `.proto` files and regenerate
- **Firewall/Network**: Ensure admin privileges handled properly
- **New features**: Add unit tests with 80% coverage minimum

## Performance Considerations

- Use async/await for I/O operations
- Implement IDisposable where appropriate
- Avoid blocking UI thread
- Use dependency injection for testability
- Cache expensive operations

## Error Handling

- Use try-catch at appropriate levels
- Log all errors with context
- Provide user-friendly error messages
- Never expose sensitive information
- Handle device disconnection gracefully