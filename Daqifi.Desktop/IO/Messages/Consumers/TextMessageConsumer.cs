using System.IO;
using System.Text;
using Daqifi.Desktop.IO.Messages.MessageTypes;
using Timer = System.Threading.Timer;

namespace Daqifi.Desktop.IO.Messages.Consumers
{
    public class TextMessageConsumer : AbstractMessageConsumer
    {
        private bool _isDisposed;
        private readonly StringBuilder _stringBuilder;
        private readonly Timer _processTimer;
        private const int PROCESS_DELAY_MS = 500; // Wait for more data before processing

        public TextMessageConsumer(Stream stream)
        {
            DataStream = stream;
            _stringBuilder = new StringBuilder();
            _processTimer = new Timer(ProcessAccumulatedData, null, Timeout.Infinite, Timeout.Infinite);
        }

        public override void Run()
        {
            var buffer = new byte[4096];

            while (Running)
            {
                try
                {
                    if (DataStream != null && DataStream.CanRead)
                    {
                        int bytesRead = DataStream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            string text = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                            lock (_stringBuilder)
                            {
                                _stringBuilder.Append(text);
                            }
                            
                            // Reset the timer each time we get data
                            _processTimer.Change(PROCESS_DELAY_MS, Timeout.Infinite);
                        }
                    }
                    Thread.Sleep(10); // Shorter sleep to be more responsive
                }
                catch (Exception ex)
                {
                    AppLogger.Error(ex, "Error reading text response");
                    if (_isDisposed) return;
                }
            }
        }

        private void ProcessAccumulatedData(object? state)
        {
            try
            {
                string accumulated;
                lock (_stringBuilder)
                {
                    accumulated = _stringBuilder.ToString().Trim();
                    _stringBuilder.Clear();
                }

                if (string.IsNullOrEmpty(accumulated)) return;

                // Log the raw received data for debugging
                AppLogger.Information($"Raw text response: {accumulated}");

                // Send the entire accumulated text as one message
                var textMessage = new TextMessage(accumulated);
                var messageArgs = new MessageEventArgs(textMessage);
                NotifyMessageReceived(this, messageArgs);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Error processing accumulated text data");
            }
        }

        public override void Stop()
        {
            try
            {
                _isDisposed = true;
                _processTimer.Dispose();
                base.Stop();
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Failed to stop TextMessageConsumer");
            }
        }
    }
} 