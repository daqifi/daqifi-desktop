using System;

namespace Daqifi.Desktop.Message
{
    public abstract class AbstractMessage : IMessage
    {
        #region Private Data
        private DateTime _timestamp = DateTime.Now;

        #endregion

        #region Properties
        public DateTime Timestamp
        {
            get { return _timestamp; }
        }

        public object Data { get; set; }

        #endregion

        public abstract byte[] GetBytes();
    }
}
