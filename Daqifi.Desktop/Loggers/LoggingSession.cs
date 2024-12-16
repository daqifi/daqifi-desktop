using Daqifi.Desktop.Channel;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

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
        public virtual ICollection<Channel.Channel> Channels { get; set; } = new List<Channel.Channel>();
        public virtual ICollection<DataSample> DataSamples { get; set; } = new List<DataSample>();

        public string Name
        {
            get => string.IsNullOrWhiteSpace(_name) ? "Session " + ID.ToString() : _name;
            set
            {
                if (_name == value) { return; }

                _name = value;
            }
        }
        #endregion

        #region Constructors
        public LoggingSession() { }

        public LoggingSession(int id, string name)
        {
            ID = id;
            SessionStart = DateTime.Now;
            Name = name;
        }
        #endregion

        #region Object Overrides
        public override bool Equals(object obj)
        {
            return obj is LoggingSession sessionObj && sessionObj.ID == ID;
        }
        #endregion
    }
}
