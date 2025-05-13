## Plan for Performance Monitor UI Simplification (Issue #153)

### 1. Project Setup & Renaming

- [x] **Branch:** Create a new feature branch (e.g., `feature/performance-monitor-ui`).
- [x] **Rename Files:**
    - [x] Rename `Daqifi.Desktop/View/Flyouts/SummaryFlyout.xaml` to `Daqifi.Desktop/View/Flyouts/PerformanceMonitorFlyout.xaml`.
    - [x] Rename `Daqifi.Desktop/View/Flyouts/SummaryFlyout.xaml.cs` to `Daqifi.Desktop/View/Flyouts/PerformanceMonitorFlyout.xaml.cs`.
    - [ ] Consider if `Daqifi.Desktop/Loggers/SummaryLogger.cs` should be renamed to `PerformanceMonitorViewModel.cs` or similar. For now, we will refactor `SummaryLogger.cs` but keep its name and location due to its `ILogger` interface and existing integration.
- [x] **Update References:**
    - [x] Update all references to the renamed files and classes in XAML and C#.
    - [x] Update the `x:Class` attribute in the XAML file.
    - [x] Update all usages of `IsLogSummaryOpen` to `IsPerformanceMonitorOpen`.
    - [x] Update all usages of `OpenLogSummaryCommand` to `OpenPerformanceMonitorCommand`.

### 2. ViewModel (`SummaryLogger.cs`) Changes
- [x] Add observable properties for all key metrics and UI state.
- [x] Wire up these properties in `NotifyResultsChanged()` for live updates.

### 3. View (`PerformanceMonitorFlyout.xaml`) Changes
- [x] Update header, description, and remove old settings/sample size controls.
- [x] Add status indicator, key metrics, and new controls.
- [x] Make detailed view collapsible.

### 4. Converters
- [x] Implement and register `OverallStatusToBrushConverter` for status indicator color.
- [x] Implement and register `MonitoringButtonContentConverter` for Start/Stop button.
- [x] Confirmed `BooleanToVisibilityConverter` is available for detailed view.

### 5. Testing
- [x] UI matches the design and is ready for live data testing.
- [ ] Test with live data to ensure metrics update and controls function as expected.

---
_Next: Test with live data and verify real-time updates and control behavior._

### 2. ViewModel (`SummaryLogger.cs`) Changes

*   **Properties for Simplified Metrics:**
    *   Modify or add properties to expose only the required data:
        *   `ActualSampleRate` (double): The current measured sample rate (likely from existing `SampleRate`).
        *   `TargetSampleRate` (double): The target sample rate. (Needs investigation on how this is determined/set. Could be a new configurable property or read from the device settings if available).
        *   `SystemLoadPercentage` (double): A value from 0-100 representing system load (e.g., derived from `AverageLatency`).
        *   `SystemLoadStatus` (enum or string): Good/Warning/Critical.
        *   `BufferPercentage` (double): Buffer fullness from 0-100 (e.g., based on `_buffer.SampleCount` relative to `_sampleSize` if `_sampleSize` represents the analysis window capacity).
        *   `BufferStatus` (enum or string): Good/Warning/Critical.
        *   `OverallSystemStatus` (enum or string): Healthy/Warning/Critical.
        *   `OverallSystemStatusMessage` (string): "System Healthy", "Performance Warning", "Performance Critical".
        *   `IsMonitoring` (bool): Bound to `Enabled` property, reflects Start/Stop state.
        *   `IsDetailedViewVisible` (bool): To control the visibility of the advanced/detailed section.
*   **Logic for Deriving Statuses:**
    *   Implement logic to calculate `SystemLoadStatus` (e.g., based on `AverageLatency` and `MaxLatency` against predefined thresholds).
    *   Implement logic to calculate `BufferStatus` (e.g., thresholds for `BufferPercentage`).
    *   Implement logic for `OverallSystemStatus` based on `SystemLoadStatus` and `BufferStatus`.
    *   Update `OnPropertyChanged` for these new derived properties.
*   **Remove Manual Sample Size from UI Control:**
    *   The UI control for `SampleSize` will be removed. The existing private `_sampleSize` field in `SummaryLogger.cs` (used for its internal averaging window) will remain. Its value might be initialized to a sensible default or potentially made configurable through a different mechanism if advanced configuration is still desired (outside the main simplified UI). The ticket states "Remove manual sample size setting (auto-adjust based on system capabilities)" – the "auto-adjust" part for the device's actual acquisition rate is likely a broader change. For this ticket, we focus on removing the UI setting for the logger's internal window and ensuring the logger works with a fixed or internally managed window size.
*   **Commands:**
    *   `ToggleMonitoringCommand`: This will likely be the existing `ToggleEnabledCommand`. Ensure its name and function align with "Start/Stop Monitoring".
    *   `ToggleDetailedViewCommand` (new `RelayCommand`): Sets `IsDetailedViewVisible`.
    *   The existing `ResetCommand` might be moved to the "detailed view" or removed if not essential for the simplified interface.

### 3. View (`PerformanceMonitorFlyout.xaml`) Changes

*   **Rename Control:** Update `x:Class` to `Daqifi.Desktop.View.Flyouts.PerformanceMonitorFlyout`.
*   **Header and Description:**
    *   Change `Flyout` `Header` from "Log Summary" to "Performance Monitor".
    *   Add a `TextBlock` below the header for "Monitor data acquisition performance".
*   **Overall Status Indicator:**
    *   Add an `Ellipse` (or similar) whose `Fill` is bound to `OverallSystemStatus` via a converter (e.g., `OverallStatusToBrushConverter`).
    *   Add a `TextBlock` next to it, bound to `OverallSystemStatusMessage`.
*   **Simplified Metrics Display:**
    *   Remove or hide the current "Settings" `GroupBox`.
    *   Create a new section for the 3 key metrics as per the mockup:
        *   **Sample Rate:** `TextBlock` for "Sample Rate: {Binding SummaryLogger.ActualSampleRate}/sec [target: {Binding SummaryLogger.TargetSampleRate}]".
        *   **System Load:** `TextBlock` for "System Load: {Binding SummaryLogger.SystemLoadPercentage, StringFormat={}{0:F0}%}" and a simple visual bar (e.g., a `ProgressBar` or a custom drawn bar) bound to `SystemLoadPercentage`. Add a `Style` or `