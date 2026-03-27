using System.Windows.Controls;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TextBox = System.Windows.Controls.TextBox;

namespace Daqifi.Desktop.View.Flyouts;

/// <summary>
/// Interaction logic for LoggedSessionFlyout.xaml
/// </summary>
public partial class LoggedSessionFlyout
{
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    public LoggedSessionFlyout()
    {
        InitializeComponent();
        _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();

    }

    private void UpdateSessionName(object sender, TextChangedEventArgs e)
    {
        if ((DataContext as DaqifiViewModel)?.SelectedLoggingSession is LoggingSession session)
        {
            var textBox = sender as TextBox;
            if (textBox != null)
            {

                var newName = textBox.Text;

                session.Name = newName;
                using var context = _loggingContext.CreateDbContext();
                var sessionToUpdate = context.Sessions.Find(session.ID);
                if (sessionToUpdate != null)
                {
                    sessionToUpdate.Name = newName;
                    context.SaveChanges();
                }
            }
        }

        LoggingManager.Instance.LoggingSessions = LoggingManager.Instance.LoadPersistedLoggingSessions();
    }
}
