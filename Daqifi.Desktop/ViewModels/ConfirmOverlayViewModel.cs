using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Daqifi.Desktop.ViewModels;

/// <summary>
/// Reusable view model for the in-pane dark confirm overlay — the destructive-action
/// confirmation pattern shared by the logged-data and profiles panes. It replaces the MahApps
/// MessageDialog (white card / blue theme) which clashed with the dark, tile-based design system.
/// <para>
/// Owns the bound overlay state (<see cref="IsOpen"/>, <see cref="Title"/>, <see cref="Message"/>,
/// <see cref="AffirmativeLabel"/>, <see cref="AffirmativeIsDestructive"/>), the affirmative/negative
/// commands, and an awaitable <see cref="ShowAsync"/> built on a
/// <see cref="TaskCompletionSource{TResult}"/>. Extracted from <c>DaqifiViewModel</c> (issue #592).
/// </para>
/// <para>
/// It is WPF-free — no <c>Application.Current</c>, dispatcher, or singletons — so a host view model
/// exposes it as a nested bindable property (<c>ConfirmOverlay</c>) and the overlay binds to
/// <c>ConfirmOverlay.*</c>; the result resolves when the two commands fire. That keeps the class
/// unit-testable in isolation: await <see cref="ShowAsync"/> and execute a command.
/// </para>
/// </summary>
public partial class ConfirmOverlayViewModel : ObservableObject
{
    #region Observable Properties
    /// <summary>True while the confirm overlay is visible.</summary>
    [ObservableProperty] private bool _isOpen;

    /// <summary>Title shown at the top of the confirm overlay card.</summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>Body message shown in the confirm overlay card.</summary>
    [ObservableProperty] private string _message = string.Empty;

    /// <summary>Label shown on the affirmative button of the confirm overlay (e.g. "DELETE").</summary>
    [ObservableProperty] private string _affirmativeLabel = "OK";

    /// <summary>
    /// When true, the affirmative button uses the danger style (red outline) instead of the
    /// accent style (filled blue). Set by destructive callers of <see cref="ShowAsync"/>.
    /// </summary>
    [ObservableProperty] private bool _affirmativeIsDestructive;
    #endregion

    #region Private Fields
    private TaskCompletionSource<bool>? _confirmTcs;
    #endregion

    #region Commands
    /// <summary>
    /// Resolves the pending <see cref="ShowAsync"/> task with <c>true</c>.
    /// Bound to the affirmative button via the generated <c>AffirmativeCommand</c>.
    /// </summary>
    [RelayCommand]
    private void Affirmative() => Complete(true);

    /// <summary>
    /// Resolves the pending <see cref="ShowAsync"/> task with <c>false</c>.
    /// Bound to the cancel button and the scrim via the generated <c>NegativeCommand</c>.
    /// </summary>
    [RelayCommand]
    private void Negative() => Complete(false);
    #endregion

    #region Public Methods
    /// <summary>
    /// Opens the confirm overlay with the supplied content and returns a task that resolves to
    /// <c>true</c> when the user chooses the affirmative button and <c>false</c> when they cancel
    /// (cancel button, scrim, or a host reset via <see cref="Cancel"/>).
    /// </summary>
    /// <param name="title">Title shown at the top of the card.</param>
    /// <param name="message">Body message shown in the card.</param>
    /// <param name="affirmativeLabel">Label for the affirmative button (e.g. "DELETE").</param>
    /// <param name="isDestructive">When true, the affirmative button uses the danger style.</param>
    public Task<bool> ShowAsync(
        string title,
        string message,
        string affirmativeLabel = "OK",
        bool isDestructive = false)
    {
        // Defensive: if a prior confirm is somehow still pending, cancel it with a negative so the
        // previous awaiter unwinds cleanly before this new one opens.
        _confirmTcs?.TrySetResult(false);

        // RunContinuationsAsynchronously avoids re-entrancy/deadlocks when the awaiter resumes
        // synchronously on the UI thread from inside the command handler that completes the task.
        _confirmTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Title = title;
        Message = message;
        AffirmativeLabel = affirmativeLabel;
        AffirmativeIsDestructive = isDestructive;
        IsOpen = true;
        return _confirmTcs.Task;
    }

    /// <summary>
    /// Cancels any pending confirmation, resolving its awaiter with <c>false</c> and closing the
    /// overlay. Used by host reset/close paths (pane navigation, view unload) so an in-flight
    /// awaiter is not stranded. Safe to call when nothing is pending.
    /// </summary>
    public void Cancel() => Complete(false);
    #endregion

    #region Private Methods
    private void Complete(bool result)
    {
        IsOpen = false;
        var tcs = _confirmTcs;
        _confirmTcs = null;
        tcs?.TrySetResult(result);
    }
    #endregion
}
