using System.IO;
using System.Linq;
using System.Text;
using Daqifi.Desktop.Channel;

namespace Daqifi.Desktop.Logger;

public class CsvLogger : ILogger
{
    #region Private Data
    private string _filename;
    private Dictionary<string, StreamWriter> _loggedChannels = new Dictionary<string, StreamWriter>();
    #endregion

    #region Properties
    public Dictionary<string, StreamWriter> LoggedChannels
    {
        get { return _loggedChannels; }
        private set { _loggedChannels = value; }
    }

    public string Filename
    {
        get { return _filename; }
        private set { _filename = value; }
    }
    #endregion

    #region Constructor
    public CsvLogger(string filename)
    {
        if (Directory.Exists(@"Logs")) Directory.Delete(@"Logs", true);
        Directory.CreateDirectory(@"Logs");
        
        Filename = "Logs/" + filename;
    }
    #endregion

    #region ILogger overrides
    public void Log(AbstractChannel channel)
    {
        //Check if we already have a series for this Channel.  If not, then create one
        if (!LoggedChannels.Keys.Contains(channel.Name))
        {
            AddChannelSeries(channel);
        }

        //LoggedChannels[channel.UID].WriteLine(channel.ActiveSample.Timestamp.ToString() + "," + channel.ActiveSample.Value.ToString());
    }

    private void AddChannelSeries(IChannel newChannel)
    {
        string channelFile = Filename.Substring(0, Filename.Count() - 4) + "_" + newChannel.Name;
        channelFile += Filename.Substring(Filename.Count() - 4, 4);
       //LoggedChannels.Add(newChannel.UID, new StreamWriter(@channelFile));
    }
    #endregion
}