using System;
using System.Collections.Generic;
using System.IO;
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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Client
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            osuInstallationPath.Text = string.Empty;
        }

        private void osuInstallationConfirm_Click(object sender, RoutedEventArgs e)
        {
            osuInstallationWrapper.IsEnabled = false;
            osuInstallationStatus.Visibility = Visibility.Collapsed;
            osuInstallationConfirm.Visibility = Visibility.Collapsed;

            programBodyWrapper.IsEnabled = true;
        }

        static string[] _osuInstallFileChecks = { "osu!.exe", "Songs" };

        private bool IsValidOsuInstall(string path)
        {
            if (!Directory.Exists(path))
                return false;

            DirectoryInfo osuInstall = new DirectoryInfo(path);
            var files = osuInstall.GetFileSystemInfos().Select(x => x.Name);

            if (!_osuInstallFileChecks.All(x => files.Contains(x)))
                return false;

            return true;
        }

        private void osuInstallationPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            var path = osuInstallationPath.Text;

            if (osuInstallationConfirm == null)
                return;

            if (IsValidOsuInstall(path))
            {
                osuInstallationStatus.Content = "Valid osu! installation detected";
                osuInstallationStatus.Foreground = Brushes.Green;
                osuInstallationConfirm.IsEnabled = true;
            }
            else
            {
                if (path == string.Empty)
                {
                    osuInstallationStatus.Content = "Input your osu install path in the box above";
                    osuInstallationStatus.Foreground = Brushes.Blue;
                }
                else
                {
                    osuInstallationStatus.Content = "No osu! installation detected at path";
                    osuInstallationStatus.Foreground = Brushes.Red;
                }

                osuInstallationConfirm.IsEnabled = false;
            }
        }
    }
}
