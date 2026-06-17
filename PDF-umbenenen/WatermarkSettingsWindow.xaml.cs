using System.Windows;

namespace PDF_umbenenen
{
    public partial class WatermarkSettingsWindow : Window
    {
        public WatermarkSettingsWindow(WatermarkSettings settings)
        {
            InitializeComponent();
            Settings = new WatermarkSettings
            {
                WatermarksEnabled = settings?.WatermarksEnabled ?? true,
                UseSmartPlacement = settings?.UseSmartPlacement ?? true,
                ExtractMaterialType = settings?.ExtractMaterialType ?? true,
                ExtractMaterialThickness = settings?.ExtractMaterialThickness ?? true,
                DataPosition = settings?.DataPosition ?? WatermarkDataPosition.LeftMiddleVertical,
                OrderPosition = settings?.OrderPosition ?? OrderNumberPosition.TopRight
            };

            WatermarksEnabledCheckBox.IsChecked = Settings.WatermarksEnabled;
            SmartPlacementCheckBox.IsChecked = Settings.UseSmartPlacement;
            ExtractMaterialTypeCheckBox.IsChecked = Settings.ExtractMaterialType;
            ExtractMaterialThicknessCheckBox.IsChecked = Settings.ExtractMaterialThickness;
        }

        public WatermarkSettings Settings { get; private set; }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Settings.WatermarksEnabled = WatermarksEnabledCheckBox.IsChecked == true;
            Settings.UseSmartPlacement = SmartPlacementCheckBox.IsChecked == true;
            Settings.ExtractMaterialType = ExtractMaterialTypeCheckBox.IsChecked == true;
            Settings.ExtractMaterialThickness = ExtractMaterialThicknessCheckBox.IsChecked == true;
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
