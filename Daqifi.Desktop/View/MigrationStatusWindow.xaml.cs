using System.Windows;

namespace Daqifi.Desktop.View;

/// <summary>
/// A small status window shown during database migration to inform the user
/// that an upgrade is in progress. Only displayed when there are pending migrations.
/// </summary>
public partial class MigrationStatusWindow : Window
{
    public MigrationStatusWindow()
    {
        InitializeComponent();
    }
}
