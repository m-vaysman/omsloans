using DevExpress.Xpf.Core;
using System;
using System.ComponentModel;
using System.IO;
using System.Windows;

namespace OMS.Loans
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : ThemedWindow
    {
        private static string LayoutPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OMS.Loans",
            "dock-layout.xml");

        public MainWindow()
        {
            InitializeComponent();
        }

        private void ThemedWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (File.Exists(LayoutPath))
            {
                DockManager.RestoreLayoutFromXml(LayoutPath);
            }
        }

        private void ThemedWindow_Closing(object sender, CancelEventArgs e)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath));
            DockManager.SaveLayoutToXml(LayoutPath);
        }
    }
}
