using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace PDF_umbenenen
{
    public partial class WatermarkPickWindow : Window
    {
        public string SelectedPath { get; private set; }

        public WatermarkPickWindow(IEnumerable<string> pdfPaths)
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);

            FilesListBox.ItemsSource = (pdfPaths ?? Enumerable.Empty<string>())
                .Where(File.Exists)
                .Select(path => Path.GetFileName(path))
                .OrderBy(name => name, System.StringComparer.OrdinalIgnoreCase)
                .ToList();

            FilesListBox.Tag = (pdfPaths ?? Enumerable.Empty<string>())
                .Where(File.Exists)
                .ToDictionary(path => Path.GetFileName(path), path => path, System.StringComparer.OrdinalIgnoreCase);

            if (FilesListBox.Items.Count > 0)
                FilesListBox.SelectedIndex = 0;
        }

        private bool TryAcceptSelection()
        {
            var name = FilesListBox.SelectedItem as string;
            var map = FilesListBox.Tag as Dictionary<string, string>;
            if (string.IsNullOrWhiteSpace(name) || map == null || !map.TryGetValue(name, out var path))
            {
                MessageBox.Show(this, "Bitte eine Zeichnung auswählen.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            SelectedPath = path;
            DialogResult = true;
            return true;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (TryAcceptSelection())
                Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void FilesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TryAcceptSelection())
                Close();
        }
    }
}
