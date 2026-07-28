namespace Daqifi.Desktop.Test.TestSupport;

/// <summary>
/// A temp directory unique to a single test-method invocation, removed best-effort by
/// <see cref="Delete"/>.
/// </summary>
/// <remarks>
/// <para>
/// Several test classes used to share a fixed path under <c>%TEMP%\DAQiFi\...</c> with fixed file
/// names inside it. Two runs on the same machine — a second git worktree, a local run alongside
/// CI — then wrote the same files and deleted each other's output, so whichever run lost the race
/// failed with "The process cannot access the file ... because it is being used by another
/// process". Because the collision happens in shared <c>[TestInitialize]</c>/<c>[TestCleanup]</c>
/// code, it also took down unrelated tests in the same class. A per-invocation GUID directory
/// removes the collision instead of narrowing the window.
/// </para>
/// <para>
/// Deliberately not <see cref="IDisposable"/>: the lifetime is owned by MSTest's
/// <c>[TestInitialize]</c>/<c>[TestCleanup]</c> pair rather than a <c>using</c> scope, and modelling
/// it as disposable only makes every test class holding one trip CA1001.
/// </para>
/// </remarks>
internal sealed class TempTestDirectory
{
    #region Constants
    private const int DELETE_ATTEMPTS = 3;
    private const int DELETE_RETRY_DELAY_MS = 50;
    #endregion

    #region Constructor
    /// <summary>
    /// Creates the directory. <paramref name="prefix"/> only makes leftovers easier to attribute;
    /// uniqueness comes from the GUID.
    /// </summary>
    public TempTestDirectory(string prefix)
    {
        FullPath = Path.Combine(Path.GetTempPath(), $"daqifi-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(FullPath);
    }
    #endregion

    #region Properties
    /// <summary>Absolute path of the directory. It exists for the lifetime of this instance.</summary>
    public string FullPath { get; }
    #endregion

    #region Public Methods
    /// <summary>Builds a path to <paramref name="fileName"/> inside this directory.</summary>
    public string GetFilePath(string fileName) => Path.Combine(FullPath, fileName);

    /// <summary>
    /// Deletes the directory and everything under it. Cleanup must never fail a test that already
    /// passed, so a handle still held open (an antivirus scan, a lagging <see cref="FileStream"/>
    /// finalizer) is retried briefly and then reported to the test output rather than thrown.
    /// </summary>
    public void Delete()
    {
        for (var attempt = 1; attempt <= DELETE_ATTEMPTS; attempt++)
        {
            try
            {
                if (Directory.Exists(FullPath))
                {
                    Directory.Delete(FullPath, recursive: true);
                }
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == DELETE_ATTEMPTS)
                {
                    Console.WriteLine($"Could not delete temp test directory '{FullPath}': {ex.Message}");
                    return;
                }

                Thread.Sleep(DELETE_RETRY_DELAY_MS);
            }
        }
    }
    #endregion
}
