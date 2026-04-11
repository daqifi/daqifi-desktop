# Claude Development Assistant Context

This document provides context for Claude to assist with the DAQiFi Desktop application development.

> **Note**: Please check the `.cursor/rules` folder periodically for any updates to development standards and incorporate those changes into this document to maintain consistency across AI-assisted development tools.

## Project Overview

DAQiFi Desktop is a Windows desktop application for communicating with DAQiFi hardware products. Our goal is to modernize data acquisition with user-friendly and intuitive products.

### Technology Stack
- .NET 9.0 and WPF for the desktop application
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
- **UDP for device discovery**: Port 30303 (broadcast queries and responses)
- **TCP for data streaming**: Port varies by device (extracted from discovery response)
- **Protocol Buffers**: Message serialization for both UDP and TCP

### Port Configuration
- **UDP Discovery Port**: 30303 (used in `DaqifiDeviceFinder` and firewall rules)
- **TCP Data Port**: Device-specific (e.g., 9760) - discovered from `message.DevicePort` in protobuf response
- **Manual IP connections**: Must use the same TCP port as discovered devices

### Network Requirements
- **Firewall Rule**: UDP port 30303 must be allowed for device discovery
- **Admin Privileges**: Required for automatic firewall configuration
- **Network Interface**: Application and device must be on same subnet for UDP broadcast discovery
- **Virtual Machines**: Use bridged networking mode, not NAT/shared network

### Key Classes
- `DaqifiDeviceFinder.cs` - Handles UDP device discovery on port 30303
- `DaqifiStreamingDevice.cs` - Manages TCP connections to devices
- `FirewallManager.cs` - Configures Windows Firewall for UDP port 30303
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

## Code Review

- **Qodo (automated reviewer)**: When Qodo leaves review comments on a PR, always reply to each comment on GitHub explaining what action was taken (fixed, partially fixed, or disagreed with and why). Use `gh api repos/{owner}/{repo}/pulls/{pr}/comments/{id}/replies` to post threaded replies.

## Common Tasks

When working on:
- **Device connectivity**: Check `/Device/` directory and ensure network configuration
- **UI changes**: Update both View and ViewModel following MVVM
- **Data persistence**: Use Entity Framework patterns
- **Protocol changes**: Update `.proto` files and regenerate
- **Firewall/Network**: Ensure admin privileges handled properly, verify ports match
- **WiFi Discovery Issues**: Check network interface detection and port configuration
- **Manual IP Connections**: Ensure TCP port matches what device discovery reports
- **Plot/Minimap changes**: Read the "Plot Rendering (OxyPlot)" section below — there are non-obvious gotchas with `InvalidatePlot`, auto-range, and feedback loops. Key files: `DatabaseLogger.cs`, `MinimapInteractionController.cs`, `MinMaxDownsampler.cs`
- **New features**: Add unit tests with 80% coverage minimum

## Performance Considerations

- Use async/await for I/O operations
- Implement IDisposable where appropriate
- Avoid blocking UI thread
- Use dependency injection for testability
- Cache expensive operations

### Plot Rendering (OxyPlot)

The logged data viewer uses viewport-aware MinMax downsampling to handle large datasets (16+ channels × 1M+ points). Key architecture decisions and gotchas:

**Viewport-aware downsampling over global decimation**: Global downsampling (e.g., LTTB to ~5000 points) conflicts with the minimap — when zoomed into a 1-minute slice of 24 hours, only ~3 points would be visible. Instead, we binary search the visible range and downsample only that slice to ~4000 points per channel. This gives full detail when zoomed in. See `MinMaxDownsampler.FindVisibleRange()` and `DatabaseLogger.UpdateMainPlotViewport()`.

**OxyPlot `InvalidatePlot(true)` vs `InvalidatePlot(false)`**: `false` re-renders from OxyPlot's internal cached point arrays. `true` forces OxyPlot to re-read `ItemsSource` and rebuild those arrays. You MUST use `true` whenever you change a series' `ItemsSource` or mutate the underlying list — otherwise the plot renders stale data. This was the root cause of a bug where zoom + minimap drag showed missing data.

**MinMax downsampling does NOT preserve original X boundaries**: Downsampled data emits points at min/max Y positions within each bucket, not at bucket edges. So the first and last X values of downsampled data may differ from the original data. This means `ResetAllAxes()` (which auto-ranges from current `ItemsSource`) can progressively shrink the visible range. Fix: explicitly compute the full time range from source data and use `axis.Zoom(min, max)` instead of auto-range.

**Feedback loops between minimap and main plot**: The minimap syncs bidirectionally with the main plot's time axis. Without a guard flag (`IsSyncingFromMinimap`), dragging the minimap triggers `AxisChanged` on the main plot, which updates the minimap, creating a render loop. Always set the guard before programmatic axis changes.

**GC pressure during interaction**: Allocating new `List<DataPoint>` per channel per frame (~960/sec at 60fps with 16 channels) causes Gen0 GC micro-stutters. Reuse cached lists per series key — clear and refill instead of allocating new ones. See `_downsampledCache` in `DatabaseLogger`.

**Throttle pattern**: Both minimap drag and main plot pan/zoom use a `DispatcherTimer` (16ms / 60fps) + dirty flag pattern. Mouse events set the flag; the timer tick does the actual work. This caps expensive operations (re-downsample + render) at 60Hz regardless of input event frequency.

## Error Handling

- Use try-catch at appropriate levels
- Log all errors with context
- Provide user-friendly error messages
- Never expose sensitive information
- Handle device disconnection gracefully
