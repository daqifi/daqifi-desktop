using System.Collections.ObjectModel;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Daqifi.Desktop.View.Flyouts;

/// <summary>
/// Interaction logic for LoggedSessionFlyout.xaml
/// </summary>
public partial class LoggedSessionFlyout
{
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    private DaqifiViewModel lViewmodel;
    public LoggedSessionFlyout()
    {
        InitializeComponent();
        _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();

    }

    private void UpdateSessionName(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if ((this.DataContext as DaqifiViewModel)?.SelectedLoggingSession is LoggingSession session)
        {
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox != null)
            {

                var newName = textBox.Text;

                session.Name = newName;
                using (var context = _loggingContext.CreateDbContext())
                {
                    var sessionToUpdate = context.Sessions.Find(session.ID);
                    if (sessionToUpdate != null)
                    {
                        sessionToUpdate.Name = newName;
                        context.SaveChanges();
                    }
                }


            }
        }
        using (var context = _loggingContext.CreateDbContext())
        {
            var savedLoggingSessions = new ObservableCollection<LoggingSession>();
            var previousSampleSessions = (from s in context.Sessions select s).ToList();
            foreach (var lsession in previousSampleSessions)
            {
                if (!savedLoggingSessions.Contains(lsession)) { savedLoggingSessions.Add(lsession); }
            }
            LoggingManager.Instance.LoggingSessions = savedLoggingSessions;
        }
    }
}