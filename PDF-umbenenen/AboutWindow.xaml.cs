using System;
using System.Diagnostics;
using System.Windows;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    /// <summary>
    /// About Window - Displays application information, version, license, and system requirements
    /// </summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);
            var demoStatus = DemoLicenseService.Evaluate();
            VersionTextBlock.Text = "Version " + AppInfo.DisplayVersion + " (" + demoStatus.VersionLine + ")";
            BuildDateTextBlock.Text = "Build Date: " + AppInfo.BuildDate;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ShowLicense_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Versuche verschiedene mögliche Pfade (Projektdateien zuerst)
                string[] possiblePaths = new[]
                {
                    // Pfad 1: Root-Lizenz im Projektstamm
                    @"E:\Programierung\PDF Sortieren\LICENSE_DE.txt",

                    // Pfad 2: Deutsche Lizenz im Projekt (relativ zum Build)
                    System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                        "..", "..", "LICENSE_DE.txt"),

                    // Pfad 3: Direkt im Debug-Ordner
                    System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                        "LICENSE.txt"),

                    // Pfad 4: Eine Ebene höher (bin/Debug -> Projektordner)
                    System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                        "..", "LICENSE.txt"),

                    // Pfad 5: Zwei Ebenen höher (Projektstamm)
                    System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                        "..", "..", "LICENSE.txt"),

                    // Pfad 6: In der Nähe der .csproj
                    System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                        "..", "..", "..", "LICENSE.txt"),

                    // Pfad 7: LICENSE_DE.txt als Fallback im Ausführungsordner
                    System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                        "LICENSE_DE.txt"),
                };

                string licenseFilePath = null;
                foreach (var path in possiblePaths)
                {
                    string fullPath = System.IO.Path.GetFullPath(path);
                    if (System.IO.File.Exists(fullPath))
                    {
                        var fileInfo = new System.IO.FileInfo(fullPath);
                        if (fileInfo.Length > 16)
                        {
                            licenseFilePath = fullPath;
                            break;
                        }
                    }
                }

                if (licenseFilePath != null && System.IO.File.Exists(licenseFilePath))
                {
                    Process.Start("notepad.exe", licenseFilePath);
                }
                else
                {
                    MessageBox.Show(
                        "Keine gültige Lizenzdatei gefunden (Datei fehlt oder ist leer).\n\n" +
                        "Gesuchte Pfade:\n" +
                        string.Join("\n", possiblePaths),
                        "Datei nicht gefunden",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Öffnen der Lizenz:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
