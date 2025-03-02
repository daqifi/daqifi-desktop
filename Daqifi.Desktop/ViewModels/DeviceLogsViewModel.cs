using System.Collections.ObjectModel;
using System.IO;
using Application = System.Windows.Application;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop.ViewModels
{
    public partial class DeviceLogsViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _busyMessage;

        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private ObservableCollection<DeviceLogInfo> _deviceLogs;

        public DeviceLogsViewModel()
        {
            DeviceLogs = new ObservableCollection<DeviceLogInfo>();
            LoadDeviceLogs();
        }

        private async Task<MessageDialogResult> ShowMessage(string title, string message, MessageDialogStyle dialogStyle)
        {
            var metroWindow = Application.Current.MainWindow as MetroWindow;
            return await metroWindow.ShowMessageAsync(title, message, dialogStyle, metroWindow.MetroDialogOptions);
        }

        private void LoadDeviceLogs()
        {
            try
            {
                IsBusy = true;
                BusyMessage = "Loading device logs...";

                // Clear existing logs
                DeviceLogs.Clear();

                // TODO: Query the device for its logs
                // For now, we'll just look for log files in a specific directory
                var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DAQifi", "DeviceLogs");
                if (Directory.Exists(logDirectory))
                {
                    foreach (var file in Directory.GetFiles(logDirectory, "*.log"))
                    {
                        var fileInfo = new FileInfo(file);
                        DeviceLogs.Add(new DeviceLogInfo
                        {
                            Name = Path.GetFileNameWithoutExtension(file),
                            FilePath = file,
                            CreatedDate = fileInfo.CreationTime,
                            Size = fileInfo.Length
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _ = ShowMessage("Error", $"Error loading device logs: {ex.Message}", MessageDialogStyle.Affirmative);
            }
            finally
            {
                IsBusy = false;
                BusyMessage = string.Empty;
            }
        }

        public async void DeleteSelectedLog(DeviceLogInfo selectedLog)
        {
            if (selectedLog == null)
                return;

            try
            {
                var result = await ShowMessage(
                    "Confirm Delete",
                    "Are you sure you want to delete this log?",
                    MessageDialogStyle.AffirmativeAndNegative);

                if (result == MessageDialogResult.Affirmative)
                {
                    IsBusy = true;
                    BusyMessage = "Deleting log...";

                    if (File.Exists(selectedLog.FilePath))
                    {
                        File.Delete(selectedLog.FilePath);
                        DeviceLogs.Remove(selectedLog);
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowMessage("Error", $"Error deleting log: {ex.Message}", MessageDialogStyle.Affirmative);
            }
            finally
            {
                IsBusy = false;
                BusyMessage = string.Empty;
            }
        }

        public async void ExportSelectedLog(DeviceLogInfo selectedLog)
        {
            if (selectedLog == null)
                return;

            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    FilterIndex = 1,
                    DefaultExt = "csv",
                    FileName = $"DeviceLog_{selectedLog.Name}_{selectedLog.CreatedDate:yyyyMMdd_HHmmss}.csv"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    IsBusy = true;
                    BusyMessage = "Exporting log...";

                    // TODO: Implement actual log file parsing and export
                    // For now, just copy the file
                    File.Copy(selectedLog.FilePath, saveFileDialog.FileName, true);

                    await ShowMessage("Success", "Export completed successfully!", MessageDialogStyle.Affirmative);
                }
            }
            catch (Exception ex)
            {
                await ShowMessage("Error", $"Error exporting log: {ex.Message}", MessageDialogStyle.Affirmative);
            }
            finally
            {
                IsBusy = false;
                BusyMessage = string.Empty;
            }
        }
    }

    public class DeviceLogInfo
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public DateTime CreatedDate { get; set; }
        public long Size { get; set; }
    }
} 