using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;
using Newtonsoft.Json.Linq;

namespace Daqifi.Desktop.Bootloader.Test
{
    [TestClass]
    public class FirmwareDownloaderTest : IDisposable
    {
        private readonly string _tempPath;
        private readonly string _daqifiTempPath;

        public FirmwareDownloaderTest()
        {
            _tempPath = Path.GetTempPath();
            _daqifiTempPath = Path.Combine(_tempPath, "DAQiFi");
            if (Directory.Exists(_daqifiTempPath))
            {
                Directory.Delete(_daqifiTempPath, true);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_daqifiTempPath))
            {
                Directory.Delete(_daqifiTempPath, true);
            }
        }

        [TestMethod]
        public void Download_SuccessfulDownload_ReturnsFirmwarePath()
        {
            // Arrange
            var mockHandler = new Mock<HttpMessageHandler>();
            var releaseJson = new JArray(
                new JObject(
                    new JProperty("assets", new JArray(
                        new JObject(
                            new JProperty("name", "DAQiFi_Nyquist.hex"),
                            new JProperty("browser_download_url", "https://example.com/DAQiFi_Nyquist.hex")
                        )
                    ))
                )
            ).ToString();

            mockHandler.Protected()
                .SetupSequence<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson)
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4, 5 })
                });

            var httpClient = new HttpClient(mockHandler.Object);
            var downloader = new FirmwareDownloader(httpClient);

            // Act
            var result = downloader.Download();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreNotEqual(string.Empty, result);
            Assert.IsTrue(File.Exists(result));
            Assert.AreEqual(Path.Combine(_daqifiTempPath, "DAQiFi_Nyquist.hex"), result);
        }

        [TestMethod]
        public void Download_FailedApiRequest_ReturnsEmptyString()
        {
            // Arrange
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

            var httpClient = new HttpClient(mockHandler.Object);
            var downloader = new FirmwareDownloader(httpClient);

            // Act
            var result = downloader.Download();

            // Assert
            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public void Download_NoHexFileInRelease_ReturnsEmptyString()
        {
            // Arrange
            var mockHandler = new Mock<HttpMessageHandler>();
            var releaseJson = new JArray(
                new JObject(
                    new JProperty("assets", new JArray(
                        new JObject(
                            new JProperty("name", "other.txt"),
                            new JProperty("browser_download_url", "https://example.com/other.txt")
                        )
                    ))
                )
            ).ToString();

            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson)
                });

            var httpClient = new HttpClient(mockHandler.Object);
            var downloader = new FirmwareDownloader(httpClient);

            // Act
            var result = downloader.Download();

            // Assert
            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public void Download_FailedFirmwareDownload_ReturnsNull()
        {
            // Arrange
            var mockHandler = new Mock<HttpMessageHandler>();
            var releaseJson = new JArray(
                new JObject(
                    new JProperty("assets", new JArray(
                        new JObject(
                            new JProperty("name", "DAQiFi_Nyquist.hex"),
                            new JProperty("browser_download_url", "https://example.com/DAQiFi_Nyquist.hex")
                        )
                    ))
                )
            ).ToString();

            mockHandler.Protected()
                .SetupSequence<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson)
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

            var httpClient = new HttpClient(mockHandler.Object);
            var downloader = new FirmwareDownloader(httpClient);

            // Act
            var result = downloader.Download();

            // Assert
            Assert.IsNull(result);
        }
    }
} 