using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Daqifi.Desktop.Channel;

namespace Daqifi.Desktop.Logger
{
    public class LoggingSession
    {
        #region Private Data
        private string _name;
        #endregion

        #region Properties
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int ID { get; set; }
        public DateTime SessionStart { get; set; }
        public virtual ICollection<IChannel> Channels { get; set; }
        public virtual ICollection<DataSample> DataSamples { get; set; }
        public string Name 
        {
            get 
            {
                return string.IsNullOrWhiteSpace(_name) ? "Session " + ID.ToString() : _name;
            }
            set 
            {
                if (_name == value) return;

                _name = value;
                using (LoggingContext context = new LoggingContext())
                {
                    context.Entry(this).State = System.Data.Entity.EntityState.Modified;
                    context.SaveChanges();
                }
            } 
        }
        #endregion

        #region Constructors
        public LoggingSession() { }

        public LoggingSession(int id)
        {
            ID = id;
            SessionStart = DateTime.Now;
        }
        #endregion

        #region Object Overrides
        public override bool Equals(object obj)
        {
            if (obj == null) return false;
            LoggingSession sessionObj = obj as LoggingSession;
            if (sessionObj == null) return false;
            else return sessionObj.ID == ID;
        }
        #endregion
    }
}
