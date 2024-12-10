﻿

namespace Daqifi.Desktop.ViewModels
{
    /// <summary>
    /// This class contains static references to all the view models in the
    /// application and provides an entry point for the bindings.
    /// </summary>
    public class ViewModelLocator
    {
        /// <summary>
        /// Initializes a new instance of the ViewModelLocator class.
        /// </summary>
        public ViewModelLocator()
        {
            ServiceLocator.SetLocatorProvider(() => SimpleIoc.Default);

            //SimpleIoc.Default.Register<DAQifiViewModel>();
        }

        public DAQifiViewModel DAQifiViewModel
        {
            get
            {
                return ServiceLocator.Current.GetInstance<DAQifiViewModel>();
            }
        }

        public static void Cleanup()
        {

        }
    }
}