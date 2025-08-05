using Daqifi.Core.Communication.Consumers;
using Daqifi.Core.Communication.Messages;
using Daqifi.Desktop.Common.Loggers;
using Google.Protobuf;
using System.IO;

namespace Daqifi.Desktop.Device.Core;

/// <summary>
/// A custom message parser that handles length-delimited protobuf messages from DAQiFi devices.
/// Unlike the standard ProtobufMessageParser, this parser expects protobuf messages with length prefixes
/// (using varint encoding) as sent by DAQiFi hardware.
/// </summary>
public class DelimitedProtobufMessageParser : IMessageParser<object>
{
    private readonly List<byte> _buffer = new();

    public IEnumerable<IInboundMessage<object>> ParseMessages(byte[] data, out int consumedBytes)
    {
        var messages = new List<IInboundMessage<object>>();
        consumedBytes = 0;

        if (data == null || data.Length == 0)
        {
            return messages;
        }

        // Add new data to buffer
        _buffer.AddRange(data);
        consumedBytes = data.Length;

        // Try to parse messages from buffer
        var bufferIndex = 0;
        while (bufferIndex < _buffer.Count)
        {
            try
            {
                // Try to parse a length-delimited message starting at bufferIndex
                var remainingBytes = _buffer.Count - bufferIndex;
                var bufferArray = _buffer.ToArray();
                
                using var stream = new MemoryStream(bufferArray, bufferIndex, remainingBytes);
                using var codedInput = new CodedInputStream(stream);
                
                // Read the varint length prefix
                if (!TryReadVarint32(codedInput, out var messageLength))
                {
                    // Not enough data for complete length prefix, wait for more data
                    break;
                }

                var lengthPrefixSize = (int)(stream.Position);
                
                // Check if we have enough data for the complete message
                if (remainingBytes < lengthPrefixSize + messageLength)
                {
                    // Not enough data for complete message, wait for more data
                    break;
                }

                // Parse the protobuf message
                var messageBytes = new byte[messageLength];
                Array.Copy(bufferArray, bufferIndex + lengthPrefixSize, messageBytes, 0, messageLength);
                
                var parsedMessage = DaqifiOutMessage.Parser.ParseFrom(messageBytes);
                if (parsedMessage != null)
                {
                    var inboundMessage = new ObjectInboundMessage(parsedMessage);
                    messages.Add(inboundMessage);
                    
                    AppLogger.Instance.Information($"[DELIMITED_PARSER] Successfully parsed DaqifiOutMessage, length: {messageLength}");
                }

                // Move buffer index past this message
                bufferIndex += lengthPrefixSize + (int)messageLength;
            }
            catch (InvalidProtocolBufferException ex)
            {
                AppLogger.Instance.Warning($"[DELIMITED_PARSER] Invalid protobuf data at buffer index {bufferIndex}: {ex.Message}");
                // Skip one byte and try again
                bufferIndex++;
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(ex, $"[DELIMITED_PARSER] Error parsing delimited protobuf message at buffer index {bufferIndex}");
                // Skip one byte and try again
                bufferIndex++;
            }
        }

        // Remove processed bytes from buffer
        if (bufferIndex > 0)
        {
            _buffer.RemoveRange(0, bufferIndex);
        }

        return messages;
    }

    private static bool TryReadVarint32(CodedInputStream input, out uint value)
    {
        value = 0;
        try
        {
            value = input.ReadUInt32();
            return true;
        }
        catch (InvalidProtocolBufferException)
        {
            // Not enough data for complete varint
            return false;
        }
        catch (Exception)
        {
            // Other reading errors
            return false;
        }
    }
}