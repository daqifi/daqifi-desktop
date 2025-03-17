using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.IO.Messages.Producers;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.DataModel.Channel;

namespace Daqifi.Desktop.Services.DeviceLogImport
{
    /// <summary>
    /// Service responsible for importing device logs into application logs
    /// </summary>
    public class DeviceLogImportService : IDeviceLogImportService
    {
        private readonly AppLogger _logger = AppLogger.Instance;
        private readonly LoggingManager _loggingManager;
        private CancellationTokenSource _cancellationTokenSource;
        private const double TickPeriod = 20E-9f; // 20ns tick period
        private readonly Dictionary<string, uint?> _previousDeviceTimestamps = new();

        public DeviceLogImportService(LoggingManager loggingManager)
        {
            _loggingManager = loggingManager;
        }

        /// <summary>
        /// Imports a device log file into the application logging session
        /// </summary>
        /// <param name="device">The device containing the log file</param>
        /// <param name="fileName">The name of the log file to import</param>
        /// <param name="progressCallback">Optional callback to report import progress</param>
        /// <returns>True if import was successful, false otherwise</returns>
        public async Task<bool> ImportDeviceLog(IStreamingDevice device, string fileName, IProgress<double> progressCallback = null)
        {
            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                _logger.Information($"Starting import of device log file: {fileName}");

                // Verify device is connected and in the correct mode
                if (device.ConnectionType != ConnectionType.Usb)
                {
                    _logger.Error("Device must be connected via USB to import logs");
                    return false;
                }

                // Stop any existing consumer
                if (device.MessageConsumer != null && device.MessageConsumer.Running)
                {
                    device.MessageConsumer.Stop();
                }

                // Create and start a new message consumer
                var stream = device.MessageConsumer?.DataStream;
                if (stream == null)
                {
                    _logger.Error("No data stream available for device");
                    return false;
                }

                device.MessageConsumer = new MessageConsumer(stream);
                if (device.MessageConsumer is MessageConsumer msgConsumer)
                {
                    msgConsumer.ClearBuffer();
                }

                // Set up message handler for Protobuf messages
                device.MessageConsumer.OnMessageReceived += HandleProtobufMessage;
                device.MessageConsumer.Start();

                // Give the consumer time to initialize
                await Task.Delay(50);

                // Send command to get the log file
                device.MessageProducer.Send(ScpiMessageProducer.GetSdFile(fileName));

                // Wait for and validate the Protobuf data
                var message = await WaitForProtobufMessage(device);
                if (message == null)
                {
                    _logger.Error("Failed to receive valid Protobuf message from device");
                    return false;
                }

                // Validate the message content
                if (!ValidateProtobufMessage(message))
                {
                    _logger.Error("Invalid Protobuf message format or content");
                    return false;
                }

                // Convert and process the message
                await ProcessProtobufMessage(device, message, progressCallback);

                _logger.Information($"Successfully imported device log file: {fileName}");
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.Warning($"Import of device log file {fileName} was cancelled");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to import device log file: {fileName}");
                return false;
            }
            finally
            {
                // Clean up message handler
                if (device.MessageConsumer != null)
                {
                    device.MessageConsumer.OnMessageReceived -= HandleProtobufMessage;
                }
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        /// <summary>
        /// Cancels any ongoing import operation
        /// </summary>
        public void CancelImport()
        {
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Handles incoming Protobuf messages
        /// </summary>
        private void HandleProtobufMessage(object sender, MessageEventArgs e)
        {
            if (e.Message.Data is DaqifiOutMessage message)
            {
                // Store the message for processing
                _lastReceivedMessage = message;
            }
        }

        private DaqifiOutMessage _lastReceivedMessage;

        /// <summary>
        /// Waits for and parses a Protobuf message from the device
        /// </summary>
        /// <param name="device">The device to receive the message from</param>
        /// <returns>The parsed Protobuf message, or null if parsing failed</returns>
        private async Task<DaqifiOutMessage> WaitForProtobufMessage(IStreamingDevice device)
        {
            try
            {
                // Wait for the message to be received and parsed
                var message = await Task.Run(() =>
                {
                    using var timeoutSource = new CancellationTokenSource(5000); // 5 second timeout
                    using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, timeoutSource.Token);

                    try
                    {
                        // Wait for message to be received
                        while (_lastReceivedMessage == null && !linkedSource.Token.IsCancellationRequested)
                        {
                            Thread.Sleep(10);
                        }

                        if (linkedSource.Token.IsCancellationRequested)
                        {
                            return null;
                        }

                        var receivedMessage = _lastReceivedMessage;
                        _lastReceivedMessage = null;
                        return receivedMessage;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to receive Protobuf message");
                        return null;
                    }
                }, _cancellationTokenSource.Token);

                return message;
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Timeout waiting for Protobuf message");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error waiting for Protobuf message");
                return null;
            }
        }

        /// <summary>
        /// Validates the content of a Protobuf message
        /// </summary>
        /// <param name="message">The message to validate</param>
        /// <returns>True if the message is valid, false otherwise</returns>
        private bool ValidateProtobufMessage(DaqifiOutMessage message)
        {
            try
            {
                // Validate required fields
                if (message.MsgTimeStamp == 0)
                {
                    _logger.Error("Message missing required timestamp");
                    return false;
                }

                if (message.DeviceSn == 0)
                {
                    _logger.Error("Message missing required device serial number");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error validating Protobuf message");
                return false;
            }
        }

        /// <summary>
        /// Processes a Protobuf message by converting it to DataSample and DeviceMessage objects
        /// </summary>
        private async Task ProcessProtobufMessage(IStreamingDevice device, DaqifiOutMessage message, IProgress<double> progressCallback)
        {
            try
            {
                var messageTimestamp = new DateTime(message.MsgTimeStamp);
                var digitalCount = 0;
                var analogCount = 0;
                var deviceId = message.DeviceSn.ToString();

                // Calculate rollover
                var previousDeviceTimestamp = _previousDeviceTimestamps.ContainsKey(deviceId) 
                    ? _previousDeviceTimestamps[deviceId].GetValueOrDefault() 
                    : message.MsgTimeStamp;
                var rollover = previousDeviceTimestamp > message.MsgTimeStamp;

                // Create device message
                var deviceMessage = new DeviceMessage
                {
                    DeviceName = device.Name,
                    DeviceSerialNo = message.DeviceSn.ToString(),
                    DeviceVersion = message.DeviceFwRev,
                    DigitalChannelCount = message.DigitalData.Length,
                    AnalogChannelCount = message.AnalogInData?.Count ?? 0,
                    TimestampTicks = message.MsgTimeStamp,
                    AppTicks = DateTime.Now.Ticks,
                    DeviceStatus = (int)message.DeviceStatus,
                    BatteryStatus = (int)message.BattStatus,
                    PowerStatus = (int)message.PwrStatus,
                    TempStatus = message.TempStatus,
                    TargetFrequency = (int)message.TimestampFreq,
                    Rollover = rollover
                };

                // Process digital channels
                var hasDigitalData = message.DigitalData.Length > 0;
                if (hasDigitalData)
                {
                    var digitalData1 = message.DigitalData.ElementAtOrDefault(0);
                    var digitalData2 = message.DigitalData.ElementAtOrDefault(1);

                    foreach (var channel in device.DataChannels.Where(c => c is DigitalChannel && c.IsActive))
                    {
                        bool bit;
                        if (digitalCount < 8)
                        {
                            bit = (digitalData1 & (1 << digitalCount)) != 0;
                        }
                        else
                        {
                            bit = (digitalData2 & (1 << digitalCount % 8)) != 0;
                        }

                        var sample = new DataSample(device, channel, messageTimestamp, Convert.ToInt32(bit));
                        _loggingManager.HandleChannelUpdate(device, sample);
                        digitalCount++;
                    }
                }

                // Process analog channels
                if (message.AnalogInData != null)
                {
                    foreach (var channel in device.DataChannels.Where(c => c is AnalogChannel && c.IsActive))
                    {
                        if (analogCount >= message.AnalogInData.Count)
                        {
                            _logger.Error($"Trying to access more analog channels than received data. Expected {analogCount} but message had {message.AnalogInData.Count}");
                            break;
                        }

                        var analogChannel = channel as AnalogChannel;
                        var scaledValue = ScaleAnalogSample(analogChannel, message.AnalogInData[analogCount]);
                        var sample = new DataSample(device, channel, messageTimestamp, scaledValue);
                        _loggingManager.HandleChannelUpdate(device, sample);
                        analogCount++;
                    }
                }

                // Handle device message
                _loggingManager.HandleDeviceMessage(device, deviceMessage);

                // Update previous timestamp for rollover calculation
                _previousDeviceTimestamps[deviceId] = message.MsgTimeStamp;

                // Report progress if callback is provided
                progressCallback?.Report(1.0);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing Protobuf message");
                throw;
            }
        }

        private static double ScaleAnalogSample(AnalogChannel channel, double analogValue)
        {
            return (analogValue / channel.Resolution) * channel.PortRange * channel.CalibrationMValue *
                   channel.InternalScaleMValue + channel.CalibrationBValue;
        }
    }
} 