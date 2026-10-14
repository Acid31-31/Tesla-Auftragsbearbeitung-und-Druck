using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    public partial class ManageCustomersWindow : Window
    {
        private List<CustomerProfile> _profiles = new List<CustomerProfile>();

        public ManageCustomersWindow()
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);
            ReloadList();
        }

        private void ReloadList()
        {
            _profiles = CustomerProfileStore.LoadAll().ToList();
            var hasProfiles = _profiles.Count > 0;

            CustomersListBox.ItemsSource = hasProfiles
                ? _profiles.Select(FormatProfileLabel).ToList()
                : null;

            CustomersListBox.Visibility = hasProfiles ? Visibility.Visible : Visibility.Collapsed;
            EmptyHintTextBlock.Visibility = hasProfiles ? Visibility.Collapsed : Visibility.Visible;

            if (hasProfiles && CustomersListBox.SelectedIndex < 0)
                CustomersListBox.SelectedIndex = 0;

            UpdateSettingsSummary();
            UpdateTeslaFoldersPanel();
            UpdateActionButtons();
        }

        private static string FormatProfileLabel(CustomerProfile profile)
        {
            if (profile == null)
                return string.Empty;

            var label = profile.DisplayName
                   + " – "
                   + (profile.RequiresExcel ? "Excel" : "Bestell-PDF");
            if (profile.IsBuiltIn)
                label += " (fest)";
            return label;
        }

        private void CustomersListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateSettingsSummary();
            UpdateTeslaFoldersPanel();
            UpdateActionButtons();
        }

        private void UpdateActionButtons()
        {
            var profile = GetSelectedProfile();
            var hasSelection = profile != null;
            var isBuiltIn = profile != null && profile.IsBuiltIn;

            ApplySettingsButton.IsEnabled = hasSelection && !isBuiltIn;
            DeleteButton.IsEnabled = hasSelection && !isBuiltIn;
        }

        private void UpdateSettingsSummary()
        {
            var profile = GetSelectedProfile();
            if (profile == null)
            {
                SettingsSummaryTextBlock.Text = _profiles.Count == 0
                    ? "Nach dem Anlegen werden hier die gespeicherten Wasserzeichen- und KI-Einstellungen angezeigt."
                    : "Profil auswählen, um gespeicherte Einstellungen anzuzeigen.";
                return;
            }

            if (CustomerProfile.IsBuiltInTeslaId(profile.Id))
            {
                var folders = CustomerProfileSettingsStore.LoadFolders(profile.Id);
                SettingsSummaryTextBlock.Text =
                    "Fester Kunde Tesla – Sortiereinstellungen sind fest verdrahtet:\r\n"
                    + "• Zeichnungen: TSL-…\r\n"
                    + "• Artikel/BG: TGA… / „verbaut in TGA…“\r\n"
                    + "• Excel-Spalten: Drawing, Requested Amount\r\n"
                    + "• Ordner unten: Bestellung und entpackte Daten (nur Tesla).\r\n"
                    + "  Bestellung: "
                    + (string.IsNullOrWhiteSpace(folders.OrderDestinationFolder) ? "(nicht gesetzt)" : folders.OrderDestinationFolder)
                    + "\r\n  Entpackt: "
                    + (string.IsNullOrWhiteSpace(folders.UnpackDestinationFolder) ? "(nicht gesetzt)" : folders.UnpackDestinationFolder);
                return;
            }

            if (CustomerProfile.IsBuiltInEcoMarkId(profile.Id))
            {
                SettingsSummaryTextBlock.Text =
                    "Fester Kunde EcoMark – Bestell-PDF-Workflow.\r\n"
                    + "• Zeichnungen: RT1 …\r\n"
                    + "• Keine Tesla-Ordner (nur für Tesla).";
                return;
            }

            SettingsSummaryTextBlock.Text = CustomerProfileSettingsStore.GetSummary(profile.Id);
        }

        private void UpdateTeslaFoldersPanel()
        {
            var profile = GetSelectedProfile();
            var isTesla = profile != null && CustomerProfile.IsBuiltInTeslaId(profile.Id);
            TeslaFoldersPanel.Visibility = isTesla ? Visibility.Visible : Visibility.Collapsed;
            if (!isTesla)
                return;

            var folders = CustomerProfileSettingsStore.LoadFolders(profile.Id);
            TeslaOrderFolderTextBox.Text = folders.OrderDestinationFolder ?? string.Empty;
            TeslaUnpackFolderTextBox.Text = folders.UnpackDestinationFolder ?? string.Empty;
        }

        private void BrowseTeslaOrderFolder_Click(object sender, RoutedEventArgs e)
        {
            BrowseAndSaveTeslaFolder(orderFolder: true);
        }

        private void BrowseTeslaUnpackFolder_Click(object sender, RoutedEventArgs e)
        {
            BrowseAndSaveTeslaFolder(orderFolder: false);
        }

        private void BrowseAndSaveTeslaFolder(bool orderFolder)
        {
            var profile = GetSelectedProfile();
            if (profile == null || !CustomerProfile.IsBuiltInTeslaId(profile.Id))
                return;

            var folders = CustomerProfileSettingsStore.LoadFolders(profile.Id);
            var current = orderFolder ? folders.OrderDestinationFolder : folders.UnpackDestinationFolder;

            var selected = FolderPickerService.PickFolder(
                this,
                orderFolder
                    ? "Zielordner für Bestell-PDF (Tesla)"
                    : "Zielordner für entpackte Daten (Tesla)",
                current);

            if (string.IsNullOrWhiteSpace(selected))
                return;

            try
            {
                Directory.CreateDirectory(selected);
                if (orderFolder)
                    folders.OrderDestinationFolder = Path.GetFullPath(selected);
                else
                    folders.UnpackDestinationFolder = Path.GetFullPath(selected);

                CustomerProfileSettingsStore.SaveFolders(profile.Id, folders);
                UpdateTeslaFoldersPanel();
                UpdateSettingsSummary();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Ordner konnte nicht gespeichert werden:\n" + ex.Message,
                    "Kunden", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private CustomerProfile GetSelectedProfile()
        {
            var index = CustomersListBox.SelectedIndex;
            if (index < 0 || index >= _profiles.Count)
                return null;

            return _profiles[index];
        }

        private void NewCustomer_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new NewCustomerWindow
            {
                Owner = this,
                HostMainWindow = Owner as MainWindow ?? Application.Current?.MainWindow as MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            ThemeWindowService.Attach(dlg);
            if (dlg.ShowDialog() == true)
                ReloadList();
        }

        private void ApplySettings_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetSelectedProfile();
            if (profile == null)
            {
                MessageBox.Show(this, "Bitte zuerst ein Profil auswählen.", "Einstellungen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (profile.IsBuiltIn || CustomerProfileStore.IsProtectedBuiltIn(profile.Id))
            {
                MessageBox.Show(this,
                    "Für feste Kunden werden keine KI-gelernten Sortiereinstellungen gespeichert.\r\n"
                    + "Bei Tesla die Ordner oben festlegen.",
                    "Einstellungen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            CustomerProfileSettingsStore.SaveSnapshotFromGlobal(profile.Id);
            UpdateSettingsSummary();
            MessageBox.Show(this,
                "Die aktuellen globalen Wasserzeichen- und KI-Einstellungen wurden für '"
                + profile.DisplayName + "' gespeichert.",
                "Einstellungen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetSelectedProfile();
            if (profile == null)
            {
                MessageBox.Show(this, "Bitte einen Kunden auswählen.", "Löschen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (profile.IsBuiltIn || CustomerProfileStore.IsProtectedBuiltIn(profile.Id))
            {
                MessageBox.Show(this,
                    "Tesla ist ein fester Kunde und kann nicht gelöscht werden.",
                    "Löschen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(this,
                "Kunde '" + profile.DisplayName + "' wirklich löschen?",
                "Löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;

            CustomerProfileStore.Delete(profile.Id);
            ReloadList();
        }
    }
}
