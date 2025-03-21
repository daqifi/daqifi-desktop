namespace Daqifi.Desktop.Channel;

public class ChannelEventArgs
{
    #region Properties
    public AbstractChannel Channel { get; }

    #endregion

    #region Constrctors
    public ChannelEventArgs(AbstractChannel channel)
    {
        Channel = channel;
    }
    #endregion
}