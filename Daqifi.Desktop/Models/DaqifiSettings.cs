using System;
using System.IO;
using System.Xml.Linq;
using Daqifi.Desktop.Loggers;

namespace Daqifi.Desktop.Models
{
    public class DaqifiSettings
    {
        #region Private Data
        private bool _canReportErrors;
        private static readonly string _appDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\DAQifi";
        private static readonly string _settingsXMLPath = _appDirectory + "\\DAQifiConfiguration.xml";
        #endregion

        #region Properties
        public bool CanReportErrors
        {
            get { return _canReportErrors; }
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

        public static DaqifiSettings Instance
        {
            get { return _instance; }
        }
        #endregion

        #region Settings Methods
        private void LoadDAQifiSettings()
        {
            try
            {
                if (!Directory.Exists(_appDirectory))
                {
                    Directory.CreateDirectory(_appDirectory);
                }

                if (!File.Exists(_settingsXMLPath))
                {
                    LoadDefaultValues();
                }

                var xml = XElement.Load(_settingsXMLPath);

                if (xml.Element("CanReportErrors") == null) return;
                bool temp;
                if (bool.TryParse(xml.Element("CanReportErrors").Value, out temp))
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
                xml.Save(_settingsXMLPath);
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(ex, "Problem Saving DAQifi Settings");
            }
        }
        #endregion
    }
}
