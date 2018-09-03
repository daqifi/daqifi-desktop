using Daqifi.Desktop.Channel;
using System.Collections.Generic;
using System.Linq;
using Daqifi.Desktop.Device;

namespace Daqifi.Desktop.Logger
{
    public class LoggingManager : ObservableObject
    {
        #region Private Variables

        private List<IChannel> _subscribedChannels;
        private List<LoggingSession> _loggingSessions;
        private bool _active;

        #endregion

        #region Properties
        public List<ILogger> Loggers { get; }

        public List<IChannel> SubscribedChannels
        {
            get => _subscribedChannels;
            private set
            {
                _subscribedChannels = value;
                NotifyPropertyChanged("SubscribedChannels");
            }
        }

        public bool Active
        {
            get => _active;
            set
            {
                //Set up the current logging session
                if(!_active)
                {
                    //Check if database has a previouse section
                    using (var context = new LoggingContext())
                    {
                        var ids = (from s in context.Sessions.AsNoTracking() select s.ID).ToList();
                        var newId = 0;
                        if(ids.Count > 0) newId = ids.Max() + 1;
                        Session = new LoggingSession(newId);
                        context.Sessions.Add(Session);
                        context.SaveChanges();
                    }
                }
                else
                {
                    //Logging session is ending
                    if (LoggingSessions == null) LoggingSessions = new List<LoggingSession>();
                    LoggingSessions.Add(Session);
                    NotifyPropertyChanged("LoggingSessions");
                }

                _active = value;
            }
        }

        public LoggingSession Session { get; private set; }

        public List<LoggingSession> LoggingSessions
        {
            get => _loggingSessions;
            set
            {
                _loggingSessions = value;
                NotifyPropertyChanged("LoggingSessions");
            }
        }
        #endregion

        #region Singleton Constructor / Initalization
        private static readonly LoggingManager instance = new LoggingManager();

        private LoggingManager()
        {
            Loggers = new List<ILogger>();
            SubscribedChannels = new List<IChannel>();
        }

        public static LoggingManager Instance => instance;

        #endregion

        #region Channel Subscription
        public void Subscribe(IChannel channel)
        {
            // Don't subscribe a channel that is already subscribed
            if (SubscribedChannels.Contains(channel)) return;

            channel.IsActive = true;
            channel.OnChannelUpdated += HandleChannelUpdate;
            SubscribedChannels.Add(channel);
            NotifyPropertyChanged("SubscribedChannels");
        }

        public void Unsubscribe(IChannel channel)
        {
            // Don't unsubscribe a channel that isn't subscribed
            if (!SubscribedChannels.Contains(channel)) return;

            channel.IsActive = false;
            channel.OnChannelUpdated -= HandleChannelUpdate;
            SubscribedChannels.Remove(channel);
            NotifyPropertyChanged("SubscribedChannels");
        }
        #endregion

        public void HandleChannelUpdate(object sender, DataSample sample)
        {
            if (!Active) return;

            sample.LoggingSessionID = Session.ID;

            //Log channel value to whatever loggers are being managed
            foreach (var logger in Loggers)
            {
                logger.Log(sample);
            }
        }

        public void AddLogger(ILogger logger)
        {
            Loggers.Add(logger);
        }
    }
}