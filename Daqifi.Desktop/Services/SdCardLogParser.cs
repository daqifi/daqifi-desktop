using Daqifi.Core.Communication.Messages;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Google.Protobuf;
using System.Globalization;
using ChannelType = Daqifi.Core.Channel.ChannelType;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;

namespace Daqifi.Desktop.Services;

/// <summary>
/// Parses SD card log files containing protobuf-encoded data samples
/// </summary>
public class SdCardLogParser
{
    #region Constants
    private const double TICK_PERIOD = 20E-9f;
    #endregion

    #region Private Fields
    private readonly AppLogger _logger = AppLogger.Instance;
    private readonly Dictionary<string, DateTime> _previousTimestamps = [];
    private readonly Dictionary<string, uint?> _previousDeviceTimestamps = [];
    #endregion

    #region Public Methods
    /// <summary>
    /// Parses a binary SD card log file and extracts data samples
    /// </summary>
    /// <param name="binaryData">The raw binary data from the SD card file</param>
    /// <param name="device">The device that created the log</param>
    /// <returns>List of data samples extracted from the log</returns>
    public List<DataSample> ParseLogFile(byte[] binaryData, IStreamingDevice device)
    {
        if (binaryData == null || binaryData.Length == 0)
        {
            throw new ArgumentException("Binary data cannot be null or empty", nameof(binaryData));
        }

        if (device == null)
        {
            throw new ArgumentNullException(nameof(device));
        }

        var samples = new List<DataSample>();
        var offset = 0;

        _logger.Information($"Starting to parse SD card log file, {binaryData.Length} bytes");

        while (offset < binaryData.Length)
        {
            try
            {
                // Try to parse a protobuf message from the current offset
                var (message, bytesRead) = TryParseProtobufMessage(binaryData, offset);

                if (message == null)
                {
                    // Skip this byte and try the next
                    offset++;
                    continue;
                }

                // Successfully parsed a message
                offset += bytesRead;

                // Extract samples from the message
                var messageSamples = ExtractSamplesFromMessage(message, device);
                samples.AddRange(messageSamples);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error parsing protobuf message at offset {offset}");
                // Try to continue parsing from the next byte
                offset++;
            }
        }

        _logger.Information($"Parsed {samples.Count} samples from SD card log file");
        return samples;
    }

    /// <summary>
    /// Resets the parser state (timestamps, etc.)
    /// </summary>
    public void Reset()
    {
        _previousTimestamps.Clear();
        _previousDeviceTimestamps.Clear();
    }
    #endregion

    #region Private Methods
    private (DaqifiOutMessage? message, int bytesRead) TryParseProtobufMessage(byte[] data, int offset)
    {
        // Protobuf uses varint encoding, so we need to find the message boundaries
        // We'll try different lengths starting from a reasonable minimum size
        const int minMessageSize = 10; // Minimum reasonable size for a protobuf message
        const int maxMessageSize = 1024; // Maximum expected size for a single message

        for (var length = minMessageSize; length <= maxMessageSize && offset + length <= data.Length; length++)
        {
            try
            {
                var messageBytes = new byte[length];
                Array.Copy(data, offset, messageBytes, 0, length);

                var message = DaqifiOutMessage.Parser.ParseFrom(messageBytes);

                // Validate that this looks like a reasonable message
                if (message.MsgTimeStamp > 0)
                {
                    return (message, length);
                }
            }
            catch
            {
                // This length didn't work, try the next
                continue;
            }
        }

        return (null, 0);
    }

    private List<DataSample> ExtractSamplesFromMessage(DaqifiOutMessage message, IStreamingDevice device)
    {
        var samples = new List<DataSample>();

        if (message.MsgTimeStamp == 0)
        {
            _logger.Warning("Protobuf message did not contain a timestamp. Will ignore message");
            return samples;
        }

        var deviceId = message.DeviceSn.ToString(CultureInfo.InvariantCulture);

        // Initialize timestamps if this is the first message for this device
        if (!_previousTimestamps.ContainsKey(deviceId))
        {
            _previousTimestamps[deviceId] = DateTime.Now;
            _previousDeviceTimestamps[deviceId] = message.MsgTimeStamp;
        }

        // Calculate the message timestamp
        var messageTimestamp = CalculateMessageTimestamp(deviceId, message.MsgTimeStamp);

        // Extract analog channel samples
        samples.AddRange(ExtractAnalogSamples(message, device, messageTimestamp));

        // Extract digital channel samples
        samples.AddRange(ExtractDigitalSamples(message, device, messageTimestamp));

        // Update the previous timestamps
        _previousTimestamps[deviceId] = messageTimestamp;
        _previousDeviceTimestamps[deviceId] = message.MsgTimeStamp;

        return samples;
    }

    private DateTime CalculateMessageTimestamp(string deviceId, uint currentDeviceTimestamp)
    {
        var previousTimestamp = _previousTimestamps[deviceId];
        var previousDeviceTimestamp = _previousDeviceTimestamps[deviceId].GetValueOrDefault();

        uint numberOfClockCyclesBetweenMessages;
        var rollover = previousDeviceTimestamp > currentDeviceTimestamp;

        if (rollover)
        {
            var numberOfCyclesToMax = uint.MaxValue - previousDeviceTimestamp;
            numberOfClockCyclesBetweenMessages = numberOfCyclesToMax + currentDeviceTimestamp;
        }
        else
        {
            numberOfClockCyclesBetweenMessages = currentDeviceTimestamp - previousDeviceTimestamp;
        }

        var secondsBetweenMessages = numberOfClockCyclesBetweenMessages * TICK_PERIOD;

        if (rollover && secondsBetweenMessages > 10)
        {
            numberOfClockCyclesBetweenMessages = previousDeviceTimestamp - currentDeviceTimestamp;
            secondsBetweenMessages = numberOfClockCyclesBetweenMessages * TICK_PERIOD * -1;
        }

        return previousTimestamp.AddSeconds(secondsBetweenMessages);
    }

    private List<DataSample> ExtractAnalogSamples(DaqifiOutMessage message, IStreamingDevice device, DateTime timestamp)
    {
        var samples = new List<DataSample>();

        if (message.AnalogInData.Count == 0)
        {
            return samples;
        }

        try
        {
            var activeAnalogChannels = device.DataChannels
                .Where(c => c.IsActive && c.Type == ChannelType.Analog)
                .Cast<AnalogChannel>()
                .OrderBy(c => c.Index)
                .ToList();

            for (var dataIndex = 0; dataIndex < message.AnalogInData.Count && dataIndex < activeAnalogChannels.Count; dataIndex++)
            {
                var channel = activeAnalogChannels[dataIndex];
                var rawValue = message.AnalogInData[dataIndex];
                var scaledValue = channel.GetScaledValue((int)rawValue);
                var sample = new DataSample(device, channel, timestamp, scaledValue);
                samples.Add(sample);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error extracting analog samples from SD card log");
        }

        return samples;
    }

    private List<DataSample> ExtractDigitalSamples(DaqifiOutMessage message, IStreamingDevice device, DateTime timestamp)
    {
        var samples = new List<DataSample>();

        if (message.DigitalData.Length == 0)
        {
            return samples;
        }

        try
        {
            var digitalData1 = message.DigitalData.ElementAtOrDefault(0);
            var digitalData2 = message.DigitalData.ElementAtOrDefault(1);

            var activeDigitalChannels = device.DataChannels
                .Where(c => c.IsActive && c.Type == ChannelType.Digital)
                .OrderBy(c => c.Index)
                .ToList();

            for (var dataIndex = 0; dataIndex < activeDigitalChannels.Count; dataIndex++)
            {
                var channel = activeDigitalChannels[dataIndex];

                bool bit;
                if (dataIndex < 8)
                {
                    bit = (digitalData1 & (1 << dataIndex)) != 0;
                }
                else
                {
                    bit = (digitalData2 & (1 << (dataIndex % 8))) != 0;
                }

                // Only create samples for digital input channels
                if (channel.Direction == ChannelDirection.Input)
                {
                    var sample = new DataSample(device, channel, timestamp, Convert.ToInt32(bit));
                    samples.Add(sample);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error extracting digital samples from SD card log");
        }

        return samples;
    }
    #endregion
}
