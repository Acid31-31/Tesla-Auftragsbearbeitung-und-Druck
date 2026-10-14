using System;
using System.Windows;
using PDF_umbenenen.Services;

namespace PDF_umbenenen
{
    public partial class DeliveryDateChangeWindow : Window
    {
        internal DateTime SelectedDate { get; private set; }

        internal DeliveryDatePrefixMode PrefixMode { get; private set; }

        public DeliveryDateChangeWindow(
            DateTime? currentDate,
            bool isFertigungstermin,
            bool mixedPrefixes,
            string singleDrawingName = null)
        {
            InitializeComponent();
            ThemeWindowService.Attach(this);
            DatePicker.SelectedDate = currentDate ?? DateTime.Today;

            if (!string.IsNullOrWhiteSpace(singleDrawingName))
            {
                Title = "Termin dieser Zeichnung ändern";
                ScopeTextBlock.Text =
                    "Neues Datum nur für diese Zeichnung:\n"
                    + singleDrawingName
                    + "\n\nDie anderen Zeichnungen bleiben unverändert.";
            }

            if (mixedPrefixes)
            {
                KeepPrefixRadio.IsChecked = true;
            }
            else if (isFertigungstermin)
            {
                FertigungsRadio.IsChecked = true;
                KeepPrefixRadio.IsChecked = false;
            }
            else
            {
                LieferRadio.IsChecked = true;
                KeepPrefixRadio.IsChecked = false;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!DatePicker.SelectedDate.HasValue)
            {
                MessageBox.Show(this, "Bitte ein Datum wählen.", "Termin ändern", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedDate = DatePicker.SelectedDate.Value.Date;
            if (FertigungsRadio.IsChecked == true)
                PrefixMode = DeliveryDatePrefixMode.Fertigungstermin;
            else if (LieferRadio.IsChecked == true)
                PrefixMode = DeliveryDatePrefixMode.Liefertermin;
            else
                PrefixMode = DeliveryDatePrefixMode.KeepExisting;

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
