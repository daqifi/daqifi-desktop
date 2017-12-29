using System;

namespace Daqifi.Desktop.Message
{
    public abstract class AbstractMessage : IMessage
    {
        #region Properties

        public DateTime Timestamp { get; } = DateTime.Now;

        public object Data { get; set; }

        #endregion

        public abstract byte[] GetBytes();
    }
}
