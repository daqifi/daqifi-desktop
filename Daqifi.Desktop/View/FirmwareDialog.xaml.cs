﻿namespace DAQifi.Desktop.View;

/// <summary>
/// Interaction logic for FirmwareDialog.xaml
/// </summary>
public partial class FirmwareDialog
{
    public FirmwareDialog()
    {
        InitializeComponent();
    }

    private void btnCancel_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }

    private void btnOk_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Close();
    }
}