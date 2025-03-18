using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.IO.Messages.Producers;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages;
using Daqifi.Desktop.Channel;

namespace Daqifi.Desktop.Services.DeviceLogImport
{
    /// <summary>
    /// Service responsible for importing device logs into application logs
    /// </summary>
    public class DeviceLogImportService : IDeviceLogImportService
    {
        private readonly AppLogger _logger = AppLogger.Instance;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly Dictionary<string, uint?> _previousDeviceTimestamps = new();

        public DeviceLogImportService()
        {
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
                if (device.MessageConsumer.Running)
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

                // Set up message handler for Protobuf messages
                device.MessageConsumer.OnMessageReceived += HandleProtobufMessage;
                device.MessageConsumer.Start();

                // Give the consumer time to initialize
                await Task.Delay(100);

                // Send command to get the log file
                device.MessageProducer.Send(ScpiMessageProducer.GetSdFile(fileName));
                
                // Give the consumer time to initialize
                await Task.Delay(10000);

                _logger.Information($"Successfully requested device log file: {fileName}");
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
                // Validate the message content
                if (!ValidateProtobufMessage(message))
                {
                    _logger.Error("Invalid Protobuf message format or content");
                    return;
                }

                // Process and log the message
                ProcessProtobufMessage(message);
                
            }
        }

        private DaqifiOutMessage _lastReceivedMessage;
        

        /// <summary>
        /// Validates the content of a Protobuf message
        /// </summary>
        /// <param name="message">The message to validate</param>
        /// <returns>True if the message is valid, false otherwise</returns>
        private bool ValidateProtobufMessage(DaqifiOutMessage message)
        {
            try
            {
                if (message == null)
                {
                    return false;
                }
                
                // Validate required fields
                if (message.MsgTimeStamp == 0)
                {
                    _logger.Error("Message missing required timestamp");
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
        /// Processes a Protobuf message and logs the decoded data
        /// </summary>
        private void ProcessProtobufMessage(DaqifiOutMessage message)
        {
            try
            {
                var digitalCount = 0;
                var analogCount = 0;
                var deviceId = message.DeviceSn.ToString();

                // Calculate rollover
                var previousDeviceTimestamp = _previousDeviceTimestamps.ContainsKey(deviceId) 
                    ? _previousDeviceTimestamps[deviceId].GetValueOrDefault() 
                    : message.MsgTimeStamp;
                var rollover = previousDeviceTimestamp > message.MsgTimeStamp;

                // Log device message details in a single statement
                _logger.Information($"Device Message Details:\n" +
                                    $"\tDevice Serial: {message.DeviceSn},\n" +
                                    $"\tDevice Version: {message.DeviceFwRev}\n" +
                                    $"\tTimestamp: {message.MsgTimeStamp}\n" +
                                    $"\tDigital Channel Count: {message.DigitalData.Length}\n" +
                                    $"\tAnalog Channel Count: {message.AnalogInData?.Count ?? 0}\n" +
                                    $"\tDevice Status: {message.DeviceStatus}\n" +
                                    $"\tBattery Status: {message.BattStatus}\n" +
                                    $"\tPower Status: {message.PwrStatus}\n" +
                                    $"\tTemperature Status: {message.TempStatus}\n" +
                                    $"\tTarget Frequency: {message.TimestampFreq}\n" +
                                    $"\tRollover: {rollover}");

                // Process and log digital channels
                var hasDigitalData = message.DigitalData.Length > 0;
                if (hasDigitalData)
                {
                    _logger.Information("Digital Channel Data:");
                    
                }

                // Process and log analog channels
                if (message.AnalogInData != null)
                {
                    _logger.Information("Analog Channel Data:");
                }

                // Update previous timestamp for rollover calculation
                _previousDeviceTimestamps[deviceId] = message.MsgTimeStamp;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing Protobuf message");
                throw;
            }
        }
    }
} 