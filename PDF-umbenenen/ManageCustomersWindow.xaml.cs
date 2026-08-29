using System.Collections.Generic;
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

            ApplySettingsButton.IsEnabled = hasProfiles;
            DeleteButton.IsEnabled = hasProfiles;

            if (hasProfiles && CustomersListBox.SelectedIndex < 0)
                CustomersListBox.SelectedIndex = 0;

            UpdateSettingsSummary();
        }

        private static string FormatProfileLabel(CustomerProfile profile)
        {
            if (profile == null)
                return string.Empty;

            return profile.DisplayName
                   + " – "
                   + (profile.RequiresExcel ? "Excel" : "Bestell-PDF");
        }

        private void CustomersListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateSettingsSummary();
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

            SettingsSummaryTextBlock.Text = CustomerProfileSettingsStore.GetSummary(profile.Id);
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
