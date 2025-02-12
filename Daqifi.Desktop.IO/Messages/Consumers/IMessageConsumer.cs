namespace Daqifi.Desktop.IO.Messages.Consumers
{
    public delegate void OnMessageReceivedHandler(object sender, MessageEventArgs e);

    public interface IMessageConsumer
    {
        bool Running { get; set; }
        Stream DataStream { get; set; }

        #region Events
        event OnMessageReceivedHandler OnMessageReceived;
        #endregion

        void Start();
        void Stop();
        void NotifyMessageReceived(object sender, MessageEventArgs e);
        void Run();
    }
}
