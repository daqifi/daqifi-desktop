# Claude Development Assistant Context

This document provides context for Claude to assist with the DAQiFi Desktop application development.

> **Note**: Please check the `.cursor/rules` folder periodically for any updates to development standards and incorporate those changes into this document to maintain consistency across AI-assisted development tools.

## Project Overview

DAQiFi Desktop is a Windows desktop application for communicating with DAQiFi hardware products. Our goal is to modernize data acquisition with user-friendly and intuitive products.

### Technology Stack
- .NET 10.0 and WPF for the desktop application
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

### Two-gate test loop
- **Fast inner gate (every edit, no hardware):** unit tests with Moq.
  ```bash
  dotnet test --filter "TestCategory!=Ui&FullyQualifiedName!~WindowsFirewallWrapperTests"
  ```
- **Integration gate (device attached, on demand):** the FlaUI UI-automation harness drives
  the real GUI against a physically connected device. Used when asked to validate a PR
  against hardware or to extend the UI scenarios.
  ```bash
  dotnet test Daqifi.Desktop.UITest
  ```
  **How it works, how to run it for a PR, the AutomationId map, and the critical
  out-of-process automation gotchas live in
  [Daqifi.Desktop.UITest/README.md](Daqifi.Desktop.UITest/README.md) — read it before
  running or extending the harness.**

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

## Release Process

The release pipeline is compatible with GitHub's **Immutable Releases** setting (which must remain enabled).

**How to cut a release:**

1. Merge all intended changes to `main`.
2. Push a version tag matching `<major>.<minor>.<patch>` (e.g. `3.3.0`). Tags with or without a
   `v` prefix both work, but the standard going forward is without:
   ```bash
   git tag 3.3.0
   git push origin 3.3.0
   ```
3. CI (`.github/workflows/release.yaml`) triggers on the tag push, builds the MSI, and creates a
   **draft** GitHub Release with the installer attached and auto-generated release notes as a
   starting point.
4. Navigate to the draft release on GitHub, edit the notes to your liking, then click
   **Publish release**.

Because the MSI is attached while the release is still a draft, publishing is safe under immutable
releases — GitHub only blocks uploads to already-published immutable releases.

The CI job fails loudly if the MSI is missing or zero bytes, so a bad build will never silently produce a release without an installer.

## Code Review

- **Qodo (automated reviewer)**: When Qodo leaves review comments on a PR, always reply to each comment on GitHub explaining what action was taken (fixed, partially fixed, or disagreed with and why). Use `gh api repos/{owner}/{repo}/pulls/{pr}/comments/{id}/replies` to post threaded replies.

## Common Tasks

When working on:
- **Device connectivity**: Check `/Device/` directory and ensure network configuration
- **UI changes**: Update both View and ViewModel following MVVM. For new or redesigned surfaces, read [docs/design-philosophy.md](docs/design-philosophy.md) first — the Channels pane is the current exemplar
- **Data persistence**: Use Entity Framework patterns
- **Protocol changes**: Update `.proto` files and regenerate
- **Firewall/Network**: Ensure admin privileges handled properly, verify ports match
- **WiFi Discovery Issues**: Check network interface detection and port configuration
- **Manual IP Connections**: Ensure TCP port matches what device discovery reports
- **Plot/Minimap changes**: Read the "Plot Rendering (OxyPlot)" section below — there are non-obvious gotchas with `InvalidatePlot`, auto-range, and feedback loops. Key files: `DatabaseLogger.cs`, `MinimapInteractionController.cs`, `MinMaxDownsampler.cs`
- **New features**: Add unit tests with 80% coverage minimum
- **UI automation / running the harness against a PR with a device, or extending the UI scenarios**: Read [Daqifi.Desktop.UITest/README.md](Daqifi.Desktop.UITest/README.md) first. It is the FlaUI integration gate (drives the real GUI out-of-process against attached hardware) and documents the unattended `DAQIFI_TEST_MODE` launch, the AutomationId map, and load-bearing gotchas (e.g. `PART_SelectedContentHost` exposes tab content to UI Automation — do not remove it)

## Performance Considerations

- Use async/await for I/O operations
- Implement IDisposable where appropriate
- Avoid blocking UI thread
- Use dependency injection for testability
- Cache expensive operations

### Plot Rendering (OxyPlot)

The logged data viewer uses viewport-aware downsampling with progressive loading for 60fps interaction with large datasets. See [ADR 001](docs/adr/001-viewport-aware-downsampling.md) for full context. Key gotchas when modifying plot code:

- **`InvalidatePlot(true)` vs `(false)`**: Use `true` whenever `ItemsSource` or its underlying list has changed — `false` renders stale cached data
- **Don't use `ResetAllAxes()` with downsampled data**: Auto-range reads from `ItemsSource`, which may have shifted X boundaries. Use explicit `axis.Zoom(min, max)` from source data
- **Guard flag for minimap sync**: Always set `IsSyncingFromMinimap` before programmatic axis changes to prevent feedback loops
- **Reuse cached lists**: Don't allocate new `List<DataPoint>` per frame — use `_downsampledCache`
- **High-fidelity DB fetch is async**: `FetchViewportDataFromDb` runs on a background thread. Cancel in-flight fetches via `_fetchCts` before starting new ones. Results marshal back to UI via `Dispatcher.Invoke`
- **Drag vs settle pattern**: During continuous interaction (minimap drag, main plot pan), use only in-memory sampled data (`highFidelity: false`). DB fetches happen on mouse-up or after 200ms idle (`highFidelity: true`)
- **Session switching must reset axes**: `ClearPlot()` calls `axis.Reset()` on all axes. Without this, the new session inherits the previous session's zoom range

## Error Handling

- Use try-catch at appropriate levels
- Log all errors with context
- Provide user-friendly error messages
- Never expose sensitive information
- Handle device disconnection gracefully
