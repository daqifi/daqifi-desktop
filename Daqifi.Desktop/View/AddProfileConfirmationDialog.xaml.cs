﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Daqifi.Desktop.View
{
    /// <summary>
    /// Interaction logic for AddProfileConfirmationDialog.xaml
    /// </summary>
    public partial class AddProfileConfirmationDialog 
    {
        public AddProfileConfirmationDialog()
        {
            InitializeComponent();       
        }

        private void ExixtingProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CreateNewProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
