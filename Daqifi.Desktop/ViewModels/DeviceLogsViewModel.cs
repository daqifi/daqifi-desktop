using System.Collections.ObjectModel;
using System.IO;
using Application = System.Windows.Application;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Device;
using System.Windows.Input;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Models;
using Daqifi.Desktop.IO.Messages.Producers;
using Daqifi.Desktop.Common.Loggers;
using Google.Protobuf;
using System.Text;
using Daqifi.Desktop.IO.Messages.Decoders;

namespace Daqifi.Desktop.ViewModels
{
    public partial class DeviceLogsViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        private AppLogger AppLogger = AppLogger.Instance;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _busyMessage;

        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private ObservableCollection<IStreamingDevice> _connectedDevices;

        [ObservableProperty]
        private IStreamingDevice _selectedDevice;

        [ObservableProperty]
        private ObservableCollection<SdCardFile> _deviceFiles;

        [ObservableProperty]
        private SdCardFile _selectedFile;

        public ICommand RefreshFilesCommand { get; private set; }
        public ICommand DownloadFileCommand { get; private set; }
        public ICommand ImportFileCommand { get; private set; }

        public DeviceLogsViewModel()
        {
            ConnectedDevices = new ObservableCollection<IStreamingDevice>();
            DeviceFiles = new ObservableCollection<SdCardFile>();
            
            // Initialize commands
            RefreshFilesCommand = new DelegateCommand(o => RefreshFiles());
            DownloadFileCommand = new DelegateCommand(o => DownloadFile(o as SdCardFile), o => CanDownloadFile());
            ImportFileCommand = new DelegateCommand(o => ImportFile(o as SdCardFile), o => CanDownloadFile());

            // Subscribe to device connection changes
            ConnectionManager.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "ConnectedDevices")
                {
                    UpdateConnectedDevices();
                }
            };

            // Initial load
            UpdateConnectedDevices();
        }

        private void UpdateConnectedDevices()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ConnectedDevices.Clear();
                foreach (var device in ConnectionManager.Instance.ConnectedDevices)
                {
                    ConnectedDevices.Add(device);
                }

                // If we have devices but none selected, select the first one
                if (SelectedDevice == null && ConnectedDevices.Any())
                {
                    SelectedDevice = ConnectedDevices.First();
                }
            });
        }

        partial void OnSelectedDeviceChanged(IStreamingDevice value)
        {
            if (value != null)
            {
                // Subscribe to file download events
                value.OnFileDownloaded += HandleFileDownloaded;
                RefreshFiles();
            }
            else
            {
                DeviceFiles.Clear();
            }
        }

        private async void HandleFileDownloaded(object sender, FileDownloadEventArgs e)
        {
            try
            {
                // Save the file content
                if (SaveFilePath != null)
                {
                    if (IsImportOperation)
                    {
                        // For import operations, save as binary file
                        // First try to convert from hex string if it looks like hex
                        byte[] bytes;
                        if (TryParseHexString(e.Content, out bytes))
                        {
                            AppLogger.Information($"Parsed content as hex string, got {bytes.Length} bytes");
                        }
                        else
                        {
                            // Otherwise save as raw bytes
                            bytes = System.Text.Encoding.UTF8.GetBytes(e.Content);
                            AppLogger.Information($"Saving raw content as bytes, length: {bytes.Length}");
                        }
                        
                        await File.WriteAllBytesAsync(SaveFilePath, bytes);
                        IsImportOperation = false;
                        
                        // Also save a raw version for debugging
                        string rawFilePath = SaveFilePath + ".raw";
                        await File.WriteAllTextAsync(rawFilePath, e.Content);
                        AppLogger.Information($"Saved raw content to {rawFilePath}");
                        
                        // Verify if it's a valid protobuf file
                        bool isValidProtobuf = await VerifyProtobufFile(SaveFilePath);
                        
                        // Try to decode the protobuf file
                        string protobufInfo = "Could not decode protobuf content.";
                        if (isValidProtobuf)
                        {
                            protobufInfo = await DecodeProtobufFile(SaveFilePath);
                        }
                        
                        string validationMessage = isValidProtobuf ? 
                            "File appears to be a valid protobuf file." : 
                            "File does not appear to be a valid protobuf file.";
                        
                        await Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            await ShowMessage("Import Complete", 
                                $"File imported successfully to {SaveFilePath}!\n{validationMessage}\n\n{protobufInfo}", 
                                MessageDialogStyle.Affirmative);
                        });
                    }
                    else
                    {
                        // For regular downloads
                        await File.WriteAllTextAsync(SaveFilePath, e.Content);
                        
                        await Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            await ShowMessage("Success", "File downloaded successfully!", MessageDialogStyle.Affirmative);
                        });
                    }
                    
                    SaveFilePath = null;
                }
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await ShowMessage("Error", $"Failed to save file: {ex.Message}", MessageDialogStyle.Affirmative);
                });
            }
            finally
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsBusy = false;
                    BusyMessage = string.Empty;
                });
            }
        }
        
        /// <summary>
        /// Tries to parse a string as hex bytes
        /// </summary>
        /// <param name="hexString">The string that might contain hex data</param>
        /// <param name="bytes">The output byte array if successful</param>
        /// <returns>True if the string was successfully parsed as hex</returns>
        private bool TryParseHexString(string hexString, out byte[] bytes)
        {
            // Remove any non-hex characters
            string cleanHex = new string(hexString.Where(c => 
                (c >= '0' && c <= '9') || 
                (c >= 'a' && c <= 'f') || 
                (c >= 'A' && c <= 'F') || 
                c == ' ' || c == '-').ToArray());
                
            // Remove spaces and dashes
            cleanHex = cleanHex.Replace(" ", "").Replace("-", "");
            
            // If we don't have a valid hex string (must be even length)
            if (string.IsNullOrWhiteSpace(cleanHex) || cleanHex.Length % 2 != 0)
            {
                bytes = Array.Empty<byte>();
                return false;
            }
            
            try
            {
                // Convert hex string to bytes
                bytes = new byte[cleanHex.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = Convert.ToByte(cleanHex.Substring(i * 2, 2), 16);
                }
                return true;
            }
            catch
            {
                bytes = Array.Empty<byte>();
                return false;
            }
        }

        /// <summary>
        /// Performs a basic validation to check if a file might be a valid protobuf file.
        /// This is a heuristic check and not a full validation.
        /// </summary>
        /// <param name="filePath">Path to the file to check</param>
        /// <returns>True if the file appears to be a valid protobuf file</returns>
        private async Task<bool> VerifyProtobufFile(string filePath)
        {
            try
            {
                // Read the file
                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                
                // Log file size for debugging
                AppLogger.Information($"Protobuf file size: {fileBytes.Length} bytes");
                
                // Protobuf files should have some minimum size
                if (fileBytes.Length < 8)
                {
                    AppLogger.Warning("File too small to be a valid protobuf file");
                    return false;
                }
                
                // Check for some common protobuf patterns
                // This is a very basic check and not foolproof
                bool hasValidWireTypes = false;
                for (int i = 0; i < fileBytes.Length - 1; i++)
                {
                    byte b = fileBytes[i];
                    // Check for valid wire types (0-5) in the lower 3 bits of a tag byte
                    int wireType = b & 0x7;
                    if (wireType <= 5 && (b >> 3) > 0)
                    {
                        hasValidWireTypes = true;
                        break;
                    }
                }
                
                AppLogger.Information($"Protobuf validation result: {hasValidWireTypes}");
                return hasValidWireTypes;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Error verifying protobuf file: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Attempts to decode a protobuf file and extract key information
        /// </summary>
        /// <param name="filePath">Path to the protobuf file</param>
        /// <returns>A string containing the decoded information</returns>
        private async Task<string> DecodeProtobufFile(string filePath)
        {
            try
            {
                // Read the file
                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                AppLogger.Information($"Attempting to decode protobuf file of size: {fileBytes.Length} bytes");
                
                // Try multiple parsing approaches
                DaqifiOutMessage? message = null;
                List<string> attemptedMethods = new List<string>();
                
                // Create a memory stream from the bytes
                using (var stream = new MemoryStream(fileBytes))
                {
                    // Approach 1: Try to parse directly
                    try
                    {
                        attemptedMethods.Add("ParseFrom");
                        stream.Position = 0;
                        message = DaqifiOutMessage.Parser.ParseFrom(stream);
                        if (message != null)
                        {
                            AppLogger.Information("Successfully parsed protobuf using ParseFrom");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning($"Failed to parse using ParseFrom: {ex.Message}");
                    }
                    
                    // Approach 2: Try to parse as delimited
                    if (message == null)
                    {
                        try
                        {
                            attemptedMethods.Add("ParseDelimitedFrom");
                            stream.Position = 0;
                            message = DaqifiOutMessage.Parser.ParseDelimitedFrom(stream);
                            if (message != null)
                            {
                                AppLogger.Information("Successfully parsed protobuf using ParseDelimitedFrom");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warning($"Failed to parse using ParseDelimitedFrom: {ex.Message}");
                        }
                    }
                    
                    // Approach 3: Try to skip some bytes and then parse
                    if (message == null)
                    {
                        try
                        {
                            attemptedMethods.Add("Skip 4 bytes + ParseFrom");
                            stream.Position = 4; // Skip potential header
                            message = DaqifiOutMessage.Parser.ParseFrom(stream);
                            if (message != null)
                            {
                                AppLogger.Information("Successfully parsed protobuf by skipping 4 bytes");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warning($"Failed to parse by skipping 4 bytes: {ex.Message}");
                        }
                    }
                    
                    // Approach 4: Try to parse as a length-prefixed message
                    if (message == null && fileBytes.Length > 4)
                    {
                        try
                        {
                            attemptedMethods.Add("Length-prefixed parsing");
                            // First 4 bytes might be the length
                            int length = BitConverter.ToInt32(fileBytes, 0);
                            if (length > 0 && length <= fileBytes.Length - 4)
                            {
                                using (var subStream = new MemoryStream(fileBytes, 4, length))
                                {
                                    message = DaqifiOutMessage.Parser.ParseFrom(subStream);
                                    if (message != null)
                                    {
                                        AppLogger.Information("Successfully parsed protobuf as length-prefixed message");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warning($"Failed to parse as length-prefixed message: {ex.Message}");
                        }
                    }
                }
                
                // If we successfully parsed the message, extract the information
                if (message != null)
                {
                    // Build a string with the key information
                    var sb = new StringBuilder();
                    sb.AppendLine("Protobuf Content:");
                    sb.AppendLine("----------------");
                    sb.AppendLine($"Parsing method: {string.Join(" -> ", attemptedMethods)}");
                    
                    // Device information
                    if (message.DeviceSn > 0)
                        sb.AppendLine($"Device Serial: {message.DeviceSn}");
                    
                    if (!string.IsNullOrEmpty(message.DevicePn))
                        sb.AppendLine($"Device Part Number: {message.DevicePn}");
                    
                    if (!string.IsNullOrEmpty(message.DeviceFwRev))
                        sb.AppendLine($"Firmware Version: {message.DeviceFwRev}");
                    
                    if (!string.IsNullOrEmpty(message.DeviceHwRev))
                        sb.AppendLine($"Hardware Version: {message.DeviceHwRev}");
                    
                    // Network information
                    if (message.IpAddr != null && message.IpAddr.Length > 0)
                        sb.AppendLine($"IP Address: {ProtobufDecoder.GetIpAddressString(message)}");
                    
                    if (message.MacAddr != null && message.MacAddr.Length > 0)
                        sb.AppendLine($"MAC Address: {ProtobufDecoder.GetMacAddressString(message)}");
                    
                    // Timestamp and status
                    if (message.MsgTimeStamp > 0)
                        sb.AppendLine($"Timestamp: {message.MsgTimeStamp}");
                    
                    if (message.DeviceStatus > 0)
                        sb.AppendLine($"Device Status: {message.DeviceStatus}");
                    
                    // Channel information
                    if (message.AnalogInPortNum > 0)
                        sb.AppendLine($"Analog Input Ports: {message.AnalogInPortNum}");
                    
                    if (message.DigitalPortNum > 0)
                        sb.AppendLine($"Digital Ports: {message.DigitalPortNum}");
                    
                    if (message.AnalogOutPortNum > 0)
                        sb.AppendLine($"Analog Output Ports: {message.AnalogOutPortNum}");
                    
                    // Data samples
                    if (message.AnalogInData.Count > 0)
                    {
                        sb.AppendLine($"Analog Input Data Samples: {message.AnalogInData.Count}");
                        sb.AppendLine($"First 5 samples: {string.Join(", ", message.AnalogInData.Take(5))}");
                    }
                    
                    if (message.DigitalData.Length > 0)
                        sb.AppendLine($"Digital Data: {BitConverter.ToString(message.DigitalData.ToByteArray())}");
                    
                    return sb.ToString();
                }
                
                // If we couldn't parse the message, return a helpful error
                return $"Could not parse as protobuf. Attempted methods: {string.Join(", ", attemptedMethods)}.\n" +
                       "The file may be corrupted or in a different format.\n" +
                       $"First 16 bytes: {BitConverter.ToString(fileBytes.Take(16).ToArray())}";
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Error decoding protobuf file: {ex.Message}");
                return $"Error decoding protobuf file: {ex.Message}";
            }
        }

        private string SaveFilePath { get; set; }
        private bool IsImportOperation { get; set; }

        private async void RefreshFiles()
        {
            if (SelectedDevice == null) return;

            try
            {
                IsBusy = true;
                BusyMessage = "Refreshing device files...";

                // Switch to SD card mode
                SelectedDevice.MessageProducer.Send(ScpiMessageProducer.DisableLan);
                await Task.Delay(100); // Give device time to process
                SelectedDevice.MessageProducer.Send(ScpiMessageProducer.EnableSdCard);
                await Task.Delay(100);

                // Request file list
                SelectedDevice.RefreshSdCardFiles();
                await Task.Delay(1000); // Wait for response

                // Update UI
                Application.Current.Dispatcher.Invoke(() =>
                {
                    DeviceFiles.Clear();
                    foreach (var file in SelectedDevice.SdCardFiles)
                    {
                        DeviceFiles.Add(file);
                    }
                });
            }
            catch (Exception ex)
            {
                await ShowMessage("Error", $"Failed to refresh device files: {ex.Message}", MessageDialogStyle.Affirmative);
            }
            finally
            {
                IsBusy = false;
                BusyMessage = string.Empty;
            }
        }

        private async void ImportFile(SdCardFile file)
        {
            if (SelectedDevice == null || file == null) return;

            try
            {
                IsBusy = true;
                BusyMessage = $"Importing file {file.FileName}...";

                // Ensure the debug directory exists
                string debugDir = @"C:\ProgramData\DAQiFi\Debug";
                Directory.CreateDirectory(debugDir);

                // Set the save path
                SaveFilePath = Path.Combine(debugDir, $"{file.FileName}.hex");
                IsImportOperation = true;

                // Download the file
                SelectedDevice.DownloadSdCardFile(file.FileName);
            }
            catch (Exception ex)
            {
                await ShowMessage("Error", $"Failed to import file: {ex.Message}", MessageDialogStyle.Affirmative);
                IsBusy = false;
                BusyMessage = string.Empty;
            }
        }

        private async void DownloadFile(SdCardFile file)
        {
            if (SelectedDevice == null || file == null) return;

            try
            {
                IsBusy = true;
                BusyMessage = "Downloading file...";

                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = file.FileName,
                    Filter = "All files (*.*)|*.*"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    SaveFilePath = saveFileDialog.FileName;
                    IsImportOperation = false;
                    SelectedDevice.DownloadSdCardFile(file.FileName);
                }
                else
                {
                    IsBusy = false;
                    BusyMessage = string.Empty;
                }
            }
            catch (Exception ex)
            {
                await ShowMessage("Error", $"Failed to download file: {ex.Message}", MessageDialogStyle.Affirmative);
                IsBusy = false;
                BusyMessage = string.Empty;
            }
        }

        private bool CanDownloadFile()
        {
            return SelectedDevice != null;
        }

        private async Task<MessageDialogResult> ShowMessage(string title, string message, MessageDialogStyle dialogStyle)
        {
            var metroWindow = Application.Current.MainWindow as MetroWindow;
            return await metroWindow.ShowMessageAsync(title, message, dialogStyle, metroWindow.MetroDialogOptions);
        }
    }
} 