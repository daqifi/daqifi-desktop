namespace Daqifi.Desktop.Channel
{
    public class ChannelUpdateMessage
    {
        public int LoggingSessionId { get; set; }
        public DataSample Sample { get; set; }

        public ChannelUpdateMessage(int loggingSessionId, DataSample sample)
        {
            LoggingSessionId = loggingSessionId;
            Sample = sample;
        }
    }
}
