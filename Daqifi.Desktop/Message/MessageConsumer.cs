﻿using System;
using System.IO;

namespace Daqifi.Desktop.Message
{
    public class MessageConsumer : AbstractMessageConsumer
    {
        #region Private Data
        private bool _isDisposed;
        #endregion

        #region Constructors
        public MessageConsumer(Stream stream)
        {
            DataStream = stream;
        }
        #endregion

        #region AbstractMessageConsumer overrides
        public override void Run()
        {
            while (Running)
            {
                try
                {
                    //Blocks until the DAQ sends a message
                    var outMessage = WiFiDAQOutMessage.ParseDelimitedFrom(DataStream);
                    var protoMessage = new ProtobufMessage(outMessage);
                    var daqMessage = new MessageEventArgs(protoMessage);
                    NotifyMessageReceived(this, daqMessage);
                }
                catch (Exception ex)
                {
                    if(_isDisposed)
                    {
                        return;
                    }

                    AppLogger.Error(ex, "Failed in AbstractMessageConsumer Run");
                }
            }
        }

        public override void Stop()
        {
            try
            {
                _isDisposed = true;
                DataStream.Close();
                base.Stop();
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Failed in AbstractMessageConsumer Stop");
            }   
        }
        #endregion
    }
}
