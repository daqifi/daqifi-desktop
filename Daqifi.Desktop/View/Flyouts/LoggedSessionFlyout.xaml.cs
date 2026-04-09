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
    public LoggedSessionFlyout()
    {
        InitializeComponent();
        _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();

    }

    private void UpdateSessionName(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if ((DataContext as DaqifiViewModel)?.SelectedLoggingSession is not LoggingSession session ||
            sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

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
