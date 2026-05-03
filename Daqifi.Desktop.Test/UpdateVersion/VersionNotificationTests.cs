using System.Net;
using System.Net.Http;
using Daqifi.Desktop.UpdateVersion;

namespace Daqifi.Desktop.Test.UpdateVersion;

[TestClass]
public class VersionNotificationTests
{
    #region Helpers

    private const string CURRENT_VERSION = "3.2.0.0";

    private static VersionNotification CreateSut(HttpStatusCode statusCode, string? body, string currentVersion = CURRENT_VERSION)
    {
        var handler = new FakeHttpMessageHandler(statusCode, body ?? string.Empty);
        return new VersionNotification(handler, currentVersion);
    }

    private static string ReleaseJson(string tagName) =>
        $"{{\"tag_name\":\"{tagName}\",\"name\":\"Release {tagName}\"}}";

    #endregion

    #region Non-success HTTP response

    [TestMethod]
    public async Task CheckForUpdatesAsync_NonSuccessStatusCode_DoesNotThrow()
    {
        // Arrange
        var sut = CreateSut(HttpStatusCode.NotFound, "<html>not found</html>");

        // Act — must not throw
        await sut.CheckForUpdatesAsync();

        // Assert
        Assert.AreEqual(0, sut.NotificationCount);
    }

    [TestMethod]
    public async Task CheckForUpdatesAsync_RateLimitResponse_DoesNotThrow()
    {
        // Arrange
        var sut = CreateSut(HttpStatusCode.Forbidden, "{\"message\":\"API rate limit exceeded\"}");

        // Act
        await sut.CheckForUpdatesAsync();

        // Assert
        Assert.AreEqual(0, sut.NotificationCount);
    }

    #endregion

    #region Null / missing tag_name

    [TestMethod]
    public async Task CheckForUpdatesAsync_NullTagName_DoesNotThrow()
    {
        // Arrange
        var sut = CreateSut(HttpStatusCode.OK, "{\"tag_name\":null,\"name\":\"Draft\"}");

        // Act
        await sut.CheckForUpdatesAsync();

        // Assert
        Assert.AreEqual(0, sut.NotificationCount);
    }

    [TestMethod]
    public async Task CheckForUpdatesAsync_MissingTagName_DoesNotThrow()
    {
        // Arrange
        var sut = CreateSut(HttpStatusCode.OK, "{\"name\":\"Draft\"}");

        // Act
        await sut.CheckForUpdatesAsync();

        // Assert
        Assert.AreEqual(0, sut.NotificationCount);
    }

    #endregion

    #region Pre-release / unparseable tag

    [TestMethod]
    public async Task CheckForUpdatesAsync_PrereleaseTag_DoesNotThrow()
    {
        // Arrange
        var sut = CreateSut(HttpStatusCode.OK, ReleaseJson("v3.2.0-rc1"));

        // Act
        await sut.CheckForUpdatesAsync();

        // Assert
        Assert.AreEqual(0, sut.NotificationCount);
    }

    [TestMethod]
    public async Task CheckForUpdatesAsync_TagWithBuildMetadata_DoesNotThrow()
    {
        // Arrange
        var sut = CreateSut(HttpStatusCode.OK, ReleaseJson("v3.2.0+hotfix"));

        // Act
        await sut.CheckForUpdatesAsync();

        // Assert
        Assert.AreEqual(0, sut.NotificationCount);
    }

    #endregion

    #region Happy path

    [TestMethod]
    public async Task CheckForUpdatesAsync_NewerVersionAvailable_SetsNotificationCount()
    {
        // Arrange
        var sut = CreateSut(HttpStatusCode.OK, ReleaseJson("v3.3.0"), currentVersion: "3.2.0.0");

        // Act
        await sut.CheckForUpdatesAsync();

        // Assert
        Assert.AreEqual(1, sut.NotificationCount);
        Assert.AreEqual("v3.3.0", sut.VersionNumber);
    }

    [TestMethod]
    public async Task CheckForUpdatesAsync_SameVersion_DoesNotSetNotificationCount()
    {
        // Arrange — 3-part tag v3.2.0 matches 4-part assembly 3.2.0.0 (same release, no notification expected)
        var sut = CreateSut(HttpStatusCode.OK, ReleaseJson("v3.2.0"), currentVersion: "3.2.0.0");

        // Act
        await sut.CheckForUpdatesAsync();

        // Assert
        Assert.AreEqual(0, sut.NotificationCount);
    }

    [TestMethod]
    public async Task CheckForUpdatesAsync_OlderVersion_DoesNotSetNotificationCount()
    {
        // Arrange
        var sut = CreateSut(HttpStatusCode.OK, ReleaseJson("v3.1.0"), currentVersion: "3.2.0.0");

        // Act
        await sut.CheckForUpdatesAsync();

        // Assert
        Assert.AreEqual(0, sut.NotificationCount);
    }

    #endregion

    #region Network failure

    [TestMethod]
    public async Task CheckForUpdatesAsync_NetworkException_DoesNotThrow()
    {
        // Arrange
        var handler = new ThrowingHttpMessageHandler();
        var sut = new VersionNotification(handler, CURRENT_VERSION);

        // Act — must not throw
        await sut.CheckForUpdatesAsync();

        // Assert
        Assert.AreEqual(0, sut.NotificationCount);
    }

    #endregion

    #region Fake handlers

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public FakeHttpMessageHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Simulated network failure");
        }
    }

    #endregion
}
