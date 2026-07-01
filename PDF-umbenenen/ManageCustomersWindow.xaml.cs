using System.Linq;
using System.Windows;
using PDF_umbenenen.Models;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    public partial class ManageCustomersWindow : Window
    {
        public ManageCustomersWindow()
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);
            ReloadList();
        }

        private void ReloadList()
        {
            CustomersListBox.ItemsSource = CustomerProfileStore.LoadAll()
                .Select(p => p.DisplayName
                           + (p.IsBuiltIn ? " (Standard)" : string.Empty)
                           + " – "
                           + (p.RequiresExcel ? "Excel" : "Bestell-PDF"))
                .ToList();
            CustomersListBox.Tag = CustomerProfileStore.LoadAll().ToList();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var index = CustomersListBox.SelectedIndex;
            var profiles = CustomersListBox.Tag as System.Collections.Generic.List<CustomerProfile>;
            if (profiles == null || index < 0 || index >= profiles.Count)
            {
                MessageBox.Show(this, "Bitte einen Kunden auswählen.", "Löschen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var profile = profiles[index];
            if (profile.IsBuiltIn)
            {
                MessageBox.Show(this, "Standardprofile können nicht gelöscht werden.", "Löschen",
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
