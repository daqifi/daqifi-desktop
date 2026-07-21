using Daqifi.Desktop.Common.Loggers;
using System.Collections.ObjectModel;
using System.IO;
using System.Xml.Linq;

namespace Daqifi.Desktop.Models;

public class DaqifiSettings
{
    #region Private Data
    private string _csvDelimiter = ",";
    // Use the shared, elevation-aware data directory (AppDataPaths): machine-wide for
    // elevated production runs, per-user for un-elevated runs. Keeps settings writable
    // (and consistent with the database/logs) instead of failing on an admin-owned file.
    private static readonly string AppDirectory = Daqifi.Desktop.Common.AppDataPaths.DataDirectory;
    private static readonly string SettingsXmlPath = AppDirectory + "\\DAQifiConfiguration.xml";
    #endregion

    #region Properties
    public ObservableCollection<string> CsvDelimiterOptions { get; } = [",", ";"];

    public string CsvDelimiter
    {
        get => _csvDelimiter;
        set
        {
            _csvDelimiter = value;
            SaveSettings();
        }
    }
    #endregion

    #region Singleton Constructor / Initalization
    private static readonly DaqifiSettings _instance = new();

    private DaqifiSettings()
    {
        LoadDAQiFiSettings();
    }

    public static DaqifiSettings Instance => _instance;

    #endregion

    #region Settings Methods
    private void LoadDAQiFiSettings()
    {
        try
        {
            if (!Directory.Exists(AppDirectory))
            {
                Directory.CreateDirectory(AppDirectory);
            }

            if (!File.Exists(SettingsXmlPath))
            {
                LoadDefaultValues();
            }

            var xml = XElement.Load(SettingsXmlPath);

            var csvDelimiterElement = xml.Element("CsvDelimiter");
            if (csvDelimiterElement != null)
            {
                _csvDelimiter = csvDelimiterElement.Value;
            }
        }
        catch(Exception ex)
        {
            AppLogger.Instance.Error(ex, "Problem Loading DAQiFi Settings");
        }
    }

    private void LoadDefaultValues()
    {
        CsvDelimiter = ",";
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            var xml = new XElement("DAQifiSettings");
            xml.Add(new XElement("CsvDelimiter", CsvDelimiter));
            xml.Save(SettingsXmlPath);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Problem Saving DAQiFi Settings");
        }
    }
    #endregion
}