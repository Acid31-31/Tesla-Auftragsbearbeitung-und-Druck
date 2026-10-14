using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using PDF_umbenenen.Services;
using MessageBox = System.Windows.MessageBox;

namespace PDF_umbenenen
{
    public partial class GeoSettingsWindow : Window
    {
        private readonly List<string> _folders = new List<string>();

        public GeoSettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            var settings = GeoSettingsStore.Load();
            _folders.Clear();
            if (settings.SearchFolders != null)
                _folders.AddRange(settings.SearchFolders.Where(f => !string.IsNullOrWhiteSpace(f)));
            TargetBaseTextBox.Text = settings.TargetBaseFolder ?? string.Empty;
            RefreshList();
        }

        private void RefreshList()
        {
            FoldersListBox.ItemsSource = null;
            FoldersListBox.ItemsSource = _folders.ToList();
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = FolderPickerService.PickFolder(this, "GEO-Suchordner wählen");
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            if (_folders.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase)))
                return;

            _folders.Add(path);
            RefreshList();
        }

        private void RemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            var selected = FoldersListBox.SelectedItems.Cast<string>().ToList();
            if (selected.Count == 0)
                return;

            foreach (var path in selected)
                _folders.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));

            RefreshList();
        }

        private void BrowseTarget_Click(object sender, RoutedEventArgs e)
        {
            var current = (TargetBaseTextBox.Text ?? string.Empty).Trim();
            var path = FolderPickerService.PickFolder(this, "Zielbasis für GEO-Kopien", current);
            if (string.IsNullOrWhiteSpace(path))
                return;

            TargetBaseTextBox.Text = path;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var target = (TargetBaseTextBox.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(target) && !Directory.Exists(target))
            {
                try
                {
                    Directory.CreateDirectory(target);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "Zielordner konnte nicht angelegt werden:\n" + ex.Message,
                        "GEO-Suche",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            GeoSettingsStore.Save(new GeoSettings
            {
                SearchFolders = _folders.ToList(),
                TargetBaseFolder = target
            });

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
