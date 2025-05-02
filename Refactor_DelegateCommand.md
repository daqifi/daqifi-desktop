# Refactor Plan: DelegateCommand to RelayCommand (Issue #152)

This document outlines the plan to replace the custom `DelegateCommand` with `CommunityToolkit.Mvvm.Input.RelayCommand` as described in [GitHub Issue #152](https://github.com/daqifi/daqifi-desktop/issues/152).

## Steps:

1.  **Install `CommunityToolkit.Mvvm`:**
    *   Verify if the `CommunityToolkit.Mvvm` NuGet package is installed in the `Daqifi.Desktop` project.
    *   If not, install the latest stable version.
2.  **Identify Usages:**
    *   Locate all files that instantiate or use `DelegateCommand`. (Completed)
    *   List these files below. (Completed)
3.  **Refactor Classes:**
    *   For each class using `DelegateCommand`:
        *   Replace `DelegateCommand` properties with methods intended to be commands.
        *   Add the `[RelayCommand]` attribute to these methods.
        *   Migrate the `Execute` logic into the method body.
        *   Migrate the `CanExecute` logic:
            *   If simple, use the `CanExecute` property of the `[RelayCommand]` attribute, pointing to a boolean property or method.
            *   If complex, keep a separate `CanExecute` method and reference it.
        *   Update calls to `RaiseCanExecuteChanged()`:
            *   If the `CanExecute` status depends on other properties in observable ViewModels, use the `[NotifyCanExecuteChangedFor]` attribute on those properties, pointing to the generated command property.
            *   Remove manual calls to `RaiseCanExecuteChanged()` where possible.
        *   Handle generic (`DelegateCommand<T>`) and async commands appropriately using `RelayCommand<T>`, `AsyncRelayCommand`, or `AsyncRelayCommand<T>`.
4.  **Delete `DelegateCommand.cs`:**
    *   Once all references are removed, delete the `Daqifi.Desktop/Commands/DelegateCommand.cs` file.
5.  **Testing:**
    *   Build the solution.
    *   Run the application.
    *   Thoroughly test all UI interactions involving commands (button clicks, menu items, etc.) to ensure they behave as expected.

## Files to Update:

*   `Daqifi.Desktop/ViewModels/SelectColorDialogViewModel.cs`
*   `Daqifi.Desktop/ViewModels/DaqifiViewModel.cs`
*   `Daqifi.Desktop/ViewModels/FirmwareDialogViewModel.cs`
*   `Daqifi.Desktop/ViewModels/ExportDialogViewModel.cs`
*   `Daqifi.Desktop/ViewModels/AddProfileConfirmationDialogViewModel.cs`
*   `Daqifi.Desktop/ViewModels/AddProfileDialogViewModel.cs`
*   `Daqifi.Desktop/ViewModels/ConnectionDialogViewModel.cs`
*   `Daqifi.Desktop/ViewModels/AddChannelDialogViewModel.cs`
*   `Daqifi.Desktop/Loggers/SummaryLogger.cs`
*   `Daqifi.Desktop/Loggers/PlotLogger.cs`
*   `Daqifi.Desktop/Loggers/DatabaseLogger.cs`
*   `Daqifi.Desktop/Commands/DelegateCommand.cs` (To be deleted) 