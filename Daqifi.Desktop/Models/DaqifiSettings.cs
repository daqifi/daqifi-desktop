using Daqifi.Desktop.Common.Loggers;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Xml.Linq;

namespace Daqifi.Desktop.Models
{
    public class DaqifiSettings
    {
        #region Private Data
        private bool _canReportErrors;
        private string _csvDelimiter;
        private static readonly string AppDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\DAQifi";
        private static readonly string SettingsXmlPath = AppDirectory + "\\DAQifiConfiguration.xml";
        #endregion

        #region Properties
        public ObservableCollection<string> CsvDelimiterOptions { get; } = new ObservableCollection<string> { ",", ";" };

        public string CsvDelimiter
        {
            get => _csvDelimiter;
            set
            {
                _csvDelimiter = value;
                SaveSettings();
            }
        }
        
        public bool CanReportErrors
        {
            get => _canReportErrors;
            set
            {
                _canReportErrors = value;
                SaveSettings();
            }
        }
        #endregion

        #region Singleton Constructor / Initalization
        private static readonly DaqifiSettings _instance = new DaqifiSettings();

        private DaqifiSettings()
        {
            LoadDAQifiSettings();
        }

        public static DaqifiSettings Instance => _instance;

        #endregion

        #region Settings Methods
        private void LoadDAQifiSettings()
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

                if (xml.Element("CanReportErrors") != null)
                {
                    if (bool.TryParse(xml.Element("CanReportErrors").Value, out bool temp))
                    {
                        _canReportErrors = temp;
                    }
                }
                
                if (xml.Element("CsvDelimiter") != null)
                {
                    _csvDelimiter = xml.Element("CsvDelimiter").Value;
                }
            }
            catch(Exception ex)
            {
                AppLogger.Instance.Error(ex, "Problem Loading DAQiFi Settings");
            }
        }

        private void LoadDefaultValues()
        {
            CanReportErrors = true;
            CsvDelimiter = ",";
            SaveSettings();
        }

        private void SaveSettings()
        {
            try
            {
                var xml = new XElement("DAQifiSettings");
                xml.Add(new XElement("CanReportErrors", CanReportErrors));
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
}
