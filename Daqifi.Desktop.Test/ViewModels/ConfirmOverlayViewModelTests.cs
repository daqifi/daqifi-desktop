using Daqifi.Desktop.ViewModels;

namespace Daqifi.Desktop.Test.ViewModels;

/// <summary>
/// Behavior contract for <see cref="ConfirmOverlayViewModel"/> — the reusable in-pane confirm
/// overlay extracted from <c>DaqifiViewModel</c> (issue #592). The overlay is driven with no WPF
/// dependency: await <see cref="ConfirmOverlayViewModel.ShowAsync"/> and fire the affirmative /
/// negative commands (or <see cref="ConfirmOverlayViewModel.Cancel"/>). Covers the opening state,
/// the two command resolutions, the re-entrancy guard, the host reset path, and the
/// RunContinuationsAsynchronously contract.
/// </summary>
[TestClass]
public class ConfirmOverlayViewModelTests
{
    #region Initial state / ShowAsync

    [TestMethod]
    public void NewOverlay_IsClosedWithDefaults()
    {
        var overlay = new ConfirmOverlayViewModel();

        Assert.IsFalse(overlay.IsOpen);
        Assert.AreEqual(string.Empty, overlay.Title);
        Assert.AreEqual(string.Empty, overlay.Message);
        Assert.AreEqual("OK", overlay.AffirmativeLabel);
        Assert.IsFalse(overlay.AffirmativeIsDestructive);
    }

    [TestMethod]
    public void ShowAsync_OpensOverlayWithSuppliedContent()
    {
        var overlay = new ConfirmOverlayViewModel();

        var task = overlay.ShowAsync(
            "Delete Confirmation",
            "Are you sure you want to delete Bench Run?",
            affirmativeLabel: "DELETE",
            isDestructive: true);

        Assert.IsTrue(overlay.IsOpen);
        Assert.AreEqual("Delete Confirmation", overlay.Title);
        Assert.AreEqual("Are you sure you want to delete Bench Run?", overlay.Message);
        Assert.AreEqual("DELETE", overlay.AffirmativeLabel);
        Assert.IsTrue(overlay.AffirmativeIsDestructive);
        Assert.IsFalse(task.IsCompleted, "The task stays pending until the user responds.");
    }

    [TestMethod]
    public void ShowAsync_DefaultsToNonDestructiveOkLabel()
    {
        var overlay = new ConfirmOverlayViewModel();

        overlay.ShowAsync("Switch profile?", "Switch to 'Bench'?");

        Assert.AreEqual("OK", overlay.AffirmativeLabel);
        Assert.IsFalse(overlay.AffirmativeIsDestructive);
        Assert.IsTrue(overlay.IsOpen);
    }

    [TestMethod]
    public async Task ReopeningNonDestructive_ResetsDestructiveStyleAndLabel()
    {
        var overlay = new ConfirmOverlayViewModel();

        // A destructive confirm leaves AffirmativeIsDestructive == true and a custom label.
        var first = overlay.ShowAsync("Delete Confirmation", "Delete it?", "DELETE", isDestructive: true);
        overlay.Cancel();
        await first;

        // Re-opening with defaults must clear both so the destructive styling/label do not leak
        // into the next non-destructive confirm — this flag drives which affirmative button
        // (accent vs danger/red) the overlay shows.
        var second = overlay.ShowAsync("Switch profile?", "Switch to 'Bench'?");

        Assert.IsFalse(overlay.AffirmativeIsDestructive, "Re-opening non-destructive must clear the danger style.");
        Assert.AreEqual("OK", overlay.AffirmativeLabel, "Re-opening must reset the affirmative label to the default.");
        Assert.AreEqual("Switch profile?", overlay.Title);
        Assert.AreEqual("Switch to 'Bench'?", overlay.Message);

        // Resolve the second confirm so the test does not leave a pending awaiter behind.
        overlay.Cancel();
        Assert.IsFalse(await second);
    }

    #endregion

    #region Command resolutions

    [TestMethod]
    public async Task AffirmativeCommand_ResolvesTaskTrueAndClosesOverlay()
    {
        var overlay = new ConfirmOverlayViewModel();
        var task = overlay.ShowAsync("T", "M", "DELETE", isDestructive: true);

        overlay.AffirmativeCommand.Execute(null);

        Assert.IsTrue(await task, "The affirmative command must resolve the awaiter with true.");
        Assert.IsFalse(overlay.IsOpen, "Choosing the affirmative button must close the overlay.");
    }

    [TestMethod]
    public async Task NegativeCommand_ResolvesTaskFalseAndClosesOverlay()
    {
        var overlay = new ConfirmOverlayViewModel();
        var task = overlay.ShowAsync("T", "M");

        overlay.NegativeCommand.Execute(null);

        Assert.IsFalse(await task, "The negative command must resolve the awaiter with false.");
        Assert.IsFalse(overlay.IsOpen, "Cancelling must close the overlay.");
    }

    [TestMethod]
    public async Task SecondCommandAfterResolution_IsNoOp_AndFirstResultWins()
    {
        var overlay = new ConfirmOverlayViewModel();
        var task = overlay.ShowAsync("T", "M", "DELETE", isDestructive: true);

        overlay.AffirmativeCommand.Execute(null);
        Assert.IsTrue(await task);

        // A late second response (stray click, or a host reset via Cancel) must not throw and must
        // not change the already-observed result. Complete() nulls the TCS before resolving it, so
        // these are no-ops rather than a double-set that would throw.
        overlay.NegativeCommand.Execute(null);
        overlay.Cancel();

        Assert.IsTrue(task.Result, "The first resolution must win; later commands are no-ops.");
        Assert.IsFalse(overlay.IsOpen);
    }

    #endregion

    #region Re-entrancy guard / host reset

    [TestMethod]
    public async Task SecondShowAsync_WhilePending_ResolvesTheFirstWithFalse()
    {
        var overlay = new ConfirmOverlayViewModel();
        var first = overlay.ShowAsync("First", "first message", "YES");

        var second = overlay.ShowAsync("Second", "second message", "DELETE", isDestructive: true);

        Assert.IsFalse(await first, "A still-pending confirm must be resolved false when a new one opens.");
        Assert.IsFalse(second.IsCompleted, "The newly opened confirm stays pending.");

        // The overlay now reflects the second confirm, and that confirm resolves independently.
        Assert.IsTrue(overlay.IsOpen);
        Assert.AreEqual("Second", overlay.Title);
        Assert.IsTrue(overlay.AffirmativeIsDestructive);

        overlay.AffirmativeCommand.Execute(null);
        Assert.IsTrue(await second);
    }

    [TestMethod]
    public async Task Cancel_ResolvesPendingConfirmFalseAndClosesOverlay()
    {
        var overlay = new ConfirmOverlayViewModel();
        var task = overlay.ShowAsync("T", "M", "DELETE", isDestructive: true);

        overlay.Cancel();

        Assert.IsFalse(await task, "Cancel must resolve the pending awaiter with false.");
        Assert.IsFalse(overlay.IsOpen);
    }

    [TestMethod]
    public void Cancel_WithNothingPending_IsANoOp()
    {
        var overlay = new ConfirmOverlayViewModel();

        overlay.Cancel(); // must not throw

        Assert.IsFalse(overlay.IsOpen);
    }

    #endregion

    #region RunContinuationsAsynchronously

    [TestMethod]
    public async Task Complete_RunsContinuationsAsynchronously()
    {
        var overlay = new ConfirmOverlayViewModel();
        var task = overlay.ShowAsync("T", "M");

        // A continuation that *requests* synchronous execution. Because the underlying TCS is
        // created with TaskCreationOptions.RunContinuationsAsynchronously, that request is
        // overridden — the continuation is queued rather than run inline on the thread that
        // completes the task. So the flag is still unset the instant Execute() returns. (Without
        // the flag this would be true: the continuation would run inline inside TrySetResult.)
        var ranInline = false;
        var continuation = task.ContinueWith(
            _ => ranInline = true,
            TaskContinuationOptions.ExecuteSynchronously);

        overlay.AffirmativeCommand.Execute(null);

        Assert.IsTrue(task.IsCompleted, "The result is set synchronously when the command fires.");
        Assert.IsFalse(ranInline,
            "RunContinuationsAsynchronously must keep continuations off the completing thread.");

        await continuation;
        Assert.IsTrue(ranInline, "The continuation still runs — just asynchronously.");
        Assert.IsTrue(task.Result);
    }

    [TestMethod]
    public async Task AwaitedResult_IsObservedCorrectly()
    {
        var overlay = new ConfirmOverlayViewModel();

        // Mirror a caller that awaits ShowConfirm(...) and resumes when a command fires.
        var resultTask = AwaitConfirm(overlay);

        overlay.NegativeCommand.Execute(null);

        Assert.IsFalse(await resultTask);
    }

    private static async Task<bool> AwaitConfirm(ConfirmOverlayViewModel overlay)
    {
        return await overlay.ShowAsync("T", "M", "DELETE", isDestructive: true);
    }

    #endregion
}
