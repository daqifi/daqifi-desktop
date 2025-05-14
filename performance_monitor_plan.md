## Plan for Performance Monitor UI Simplification (Issue #153)

### 1. Project Setup & Renaming

- [x] **Branch:** Create a new feature branch (e.g., `feature/performance-monitor-ui`).
- [x] **Rename Files:**
    - [x] Rename `Daqifi.Desktop/View/Flyouts/SummaryFlyout.xaml` to `Daqifi.Desktop/View/Flyouts/PerformanceMonitorFlyout.xaml`.
    - [x] Rename `Daqifi.Desktop/View/Flyouts/SummaryFlyout.xaml.cs` to `Daqifi.Desktop/View/Flyouts/PerformanceMonitorFlyout.xaml.cs`.
    - [x] Rename `Daqifi.Desktop/Loggers/SummaryLogger.cs` to `Daqifi.Desktop/Loggers/PerformanceMonitorViewModel.cs` and the class `SummaryLogger` to `PerformanceMonitorViewModel`.
- [x] **Update References:**
    - [x] Update all references to the renamed files and classes in XAML and C#.
    - [x] Update the `x:Class` attribute in the XAML file.
    - [x] Update all usages of `IsLogSummaryOpen` to `IsPerformanceMonitorOpen`.
    - [x] Update all usages of `OpenLogSummaryCommand` to `OpenPerformanceMonitorCommand`.

### 2. ViewModel (`PerformanceMonitorViewModel.cs`) Changes
- [x] Add observable properties for all key metrics and UI state.
- [x] Wire up these properties in `NotifyResultsChanged()` for live updates.
- [x] Refactor logic for status derivations and performance calculations.

### 3. View (`PerformanceMonitorFlyout.xaml`) Changes
- [x] Update header, description, and remove old settings/sample size controls.
- [x] Add status indicator, key metrics (Sample Rate, Avg. App Lag, Sampling Efficiency), and new controls.
- [x] Remove detailed view entirely for a more focused UI.

### 4. Converters
- [x] Implement and register `OverallStatusToBrushConverter` for status indicator color.
- [x] Implement and register `MonitoringButtonContentConverter` for Start/Stop button.
- [x] `BooleanToVisibilityConverter` for detailed view is no longer needed as the detailed view was removed.

### 5. Testing
- [x] UI matches the design and is ready for live data testing.
- [x] Test with live data to ensure metrics update and controls function as expected. Identified and fixed issues with Avg. App Lag calculation and initial Target Sample Rate.

---
_All core tasks for initial Performance Monitor simplification completed._

### 2. ViewModel (`PerformanceMonitorViewModel.cs`) Changes (Detailed)

*   **Properties for Simplified Metrics:**
    *   [x] `ActualSampleRate` (double): The current measured sample rate.
    *   [x] `TargetSampleRate` (double): The target sample rate, calculated from subscribed channels' device frequencies.
    *   [x] `AverageAppProcessingLagMs` (double): Replaced `SystemLoadPercentage`. Represents average application processing lag in milliseconds.
    *   [x] `AppProcessingLagStatus` (string): Good/Warning/Critical based on `AverageAppProcessingLagMs`.
    *   [x] `SamplingEfficiencyPercentage` (double): New metric (Actual Rate / Target Rate) * 100.
    *   [x] `SamplingEfficiencyStatus` (string): Good/Warning/Critical based on `SamplingEfficiencyPercentage`.
    *   [x] ~~`BufferPercentage` (double): Removed.~~
    *   [x] ~~`BufferStatus` (enum or string): Removed.~~
    *   [x] `OverallSystemStatus` (string): Healthy/Warning/Critical.
    *   [x] `OverallSystemStatusMessage` (string): "System Healthy", "Performance Warning", "Performance Critical".
    *   [x] `Enabled` (bool): (Previously `IsMonitoring`) Reflects Start/Stop state.
    *   [x] ~~`IsDetailedViewVisible` (bool): Removed.~~
*   **Logic for Deriving Statuses:**
    *   [x] Implement logic to calculate `AppProcessingLagStatus` based on `AverageAppProcessingLagMs` against predefined thresholds (e.g., Good <= 1ms, Warning <= 5ms, Critical > 5ms).
    *   [x] Implement logic to calculate `SamplingEfficiencyStatus` (e.g., Good >= 95%, Warning 80-94%, Critical < 80%).
    *   [x] ~~Implement logic to calculate `BufferStatus`: Removed.~~
    *   [x] Implement logic for `OverallSystemStatus` based on `AppProcessingLagStatus` and `SamplingEfficiencyStatus`.
    *   [x] Update `OnPropertyChanged` for these properties.
*   **Remove Manual Sample Size from UI Control:**
    *   [x] The UI control for `SampleSize` removed. The internal `_sampleSize` for the ViewModel's averaging window remains.
*   **Commands:**
    *   [x] `ToggleEnabledCommand`: (Previously `ToggleMonitoringCommand`) Existing command used for "Start/Stop Monitoring".
    *   [x] ~~`ToggleDetailedViewCommand`: Removed.~~
    *   [x] ~~`ResetCommand`: Removed.~~

### 3. View (`PerformanceMonitorFlyout.xaml`) Changes (Detailed)

*   [x] **Rename Control:** Update `x:Class` to `Daqifi.Desktop.View.Flyouts.PerformanceMonitorFlyout`.
*   [x] **Header and Description:**
    *   [x] Change `Flyout` `Header` to "Performance Monitor".
    *   [x] Add `TextBlock` for "Monitor data acquisition performance".
*   [x] **Overall Status Indicator:**
    *   [x] Add `Ellipse` whose `Fill` is bound to `OverallSystemStatus` via `OverallStatusToBrushConverter`.
    *   [x] Add `TextBlock` next to it, bound to `OverallSystemStatusMessage`.
*   **Simplified Metrics Display:**
    *   [x] Remove current "Settings" `GroupBox`.
    *   [x] Create new section for key metrics:
        *   [x] **Sample Rate:** `TextBlock` for "Sample Rate: {Binding PerformanceMonitorViewModel.ActualSampleRate}/sec [target: {Binding PerformanceMonitorViewModel.TargetSampleRate}]".
        *   [x] **Avg. App Lag:** `TextBlock` for "Avg. App Lag: {Binding PerformanceMonitorViewModel.AverageAppProcessingLagMs, StringFormat='{}{0:F2} ms'}". The ProgressBar for this was removed.
        *   [x] **Sampling Efficiency:** `TextBlock` for "Sampling Efficiency: {Binding PerformanceMonitorViewModel.SamplingEfficiencyPercentage, StringFormat='{}{0:F0}%'}" and a `ProgressBar` bound to `SamplingEfficiencyPercentage`.
        *   [x] ~~**Buffer:** TextBlock and ProgressBar for Buffer display removed.~~
*   [x] ~~**Detailed View Section:** Removed entirely.~~
    *   [x] ~~Remove "View Details" `ToggleButton`.~~
    *   [x] ~~Remove the `Grid` containing detailed stats (`Last Update`, `Elapsed Time`, `Delta Min/Max/Avg`, etc.).~~
*   [x] **Start/Stop Monitoring Button:**
    *   [x] Use a `Button` with `Content` bound to `Enabled` via `MonitoringButtonContentConverter` (shows "Start Monitoring" or "Stop Monitoring").
    *   [x] `Command` bound to `ToggleEnabledCommand`.