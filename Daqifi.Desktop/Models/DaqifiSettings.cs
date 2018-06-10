using Daqifi.Desktop.Common.Loggers;
using System;
using System.IO;
using System.Xml.Linq;

namespace Daqifi.Desktop.Models
{
    public class DaqifiSettings
    {
        #region Private Data
        private bool _canReportErrors;
        private static readonly string AppDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\DAQifi";
        private static readonly string SettingsXmlPath = AppDirectory + "\\DAQifiConfiguration.xml";
        #endregion

        #region Properties
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

                if (xml.Element("CanReportErrors") == null) return;
                if (bool.TryParse(xml.Element("CanReportErrors").Value, out bool temp))
                {
                    _canReportErrors = temp;
                }
            }
            catch(Exception ex)
            {
                AppLogger.Instance.Error(ex, "Problem Loading DAQifi Settings");
            }
        }

        private void LoadDefaultValues()
        {
            CanReportErrors = true;
            SaveSettings();
        }

        private void SaveSettings()
        {
            try
            {
                var xml = new XElement("DAQifiSettings");
                xml.Add(new XElement("CanReportErrors", CanReportErrors));
                xml.Save(SettingsXmlPath);
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(ex, "Problem Saving DAQifi Settings");
            }
        }
        #endregion
    }
}
