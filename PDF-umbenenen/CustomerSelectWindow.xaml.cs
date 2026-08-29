using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PDF_umbenenen.Models;

namespace PDF_umbenenen
{
    public partial class CustomerSelectWindow : Window
    {
        private readonly List<CustomerProfile> _profiles;

        public CustomerProfile SelectedProfile { get; private set; }

        public CustomerSelectWindow(IReadOnlyList<CustomerProfile> profiles)
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);
            _profiles = (profiles ?? new CustomerProfile[0]).ToList();
            CustomersListBox.ItemsSource = _profiles
                .Select(p => p.DisplayName + " – " + (p.RequiresExcel ? "Excel" : "Bestell-PDF"))
                .ToList();

            if (_profiles.Count > 0)
                CustomersListBox.SelectedIndex = 0;
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            ConfirmSelection();
        }

        private void CustomersListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ConfirmSelection();
        }

        private void ConfirmSelection()
        {
            var index = CustomersListBox.SelectedIndex;
            if (index < 0 || index >= _profiles.Count)
            {
                MessageBox.Show(this, "Bitte einen Kunden auswählen.", "Auswahl",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedProfile = _profiles[index];
            DialogResult = true;
            Close();
        }
    }
}
