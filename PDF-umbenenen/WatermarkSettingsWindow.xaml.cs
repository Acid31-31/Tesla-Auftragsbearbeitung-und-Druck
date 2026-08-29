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
                MaterialThicknessPlacement = MaterialThicknessPlacement.BottomLeftFixed,
                BgWatermarkEnabled = settings?.BgWatermarkEnabled ?? true,
                BgPlacement = BgPlacement.RightVerticalFixed,
                ExtractPowderCoating = settings?.ExtractPowderCoating ?? false,
                PowderCoatingShowRalNumber = settings?.PowderCoatingShowRalNumber ?? true,
                ExtractAnodizing = settings?.ExtractAnodizing ?? false,
                DataPosition = settings?.DataPosition ?? WatermarkDataPosition.LeftMiddleVertical,
                OrderPosition = settings?.OrderPosition ?? OrderNumberPosition.TopRight,
                DeliveryDatePosition = settings?.DeliveryDatePosition ?? DeliveryDatePosition.TopLeft,
                MirrorBottomToTopBand = settings?.MirrorBottomToTopBand ?? true
            };

            WatermarksEnabledCheckBox.IsChecked = Settings.WatermarksEnabled;
            SmartPlacementCheckBox.IsChecked = Settings.UseSmartPlacement;
            ExtractMaterialTypeCheckBox.IsChecked = Settings.ExtractMaterialType;
            ExtractMaterialThicknessCheckBox.IsChecked = Settings.ExtractMaterialThickness;
            BgWatermarkEnabledCheckBox.IsChecked = Settings.BgWatermarkEnabled;
            ExtractPowderCoatingCheckBox.IsChecked = Settings.ExtractPowderCoating;
            PowderCoatingShowRalNumberCheckBox.IsChecked = Settings.PowderCoatingShowRalNumber;
            ExtractAnodizingCheckBox.IsChecked = Settings.ExtractAnodizing;
            MirrorBottomToTopBandCheckBox.IsChecked = Settings.MirrorBottomToTopBand;

            UpdatePowderSubOptionState();
            ExtractPowderCoatingCheckBox.Checked += (_, __) => UpdatePowderSubOptionState();
            ExtractPowderCoatingCheckBox.Unchecked += (_, __) => UpdatePowderSubOptionState();
        }

        public WatermarkSettings Settings { get; private set; }

        private void UpdatePowderSubOptionState()
        {
            PowderCoatingShowRalNumberCheckBox.IsEnabled = ExtractPowderCoatingCheckBox.IsChecked == true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Settings.WatermarksEnabled = WatermarksEnabledCheckBox.IsChecked == true;
            Settings.UseSmartPlacement = SmartPlacementCheckBox.IsChecked == true;
            Settings.ExtractMaterialType = ExtractMaterialTypeCheckBox.IsChecked == true;
            Settings.ExtractMaterialThickness = ExtractMaterialThicknessCheckBox.IsChecked == true;
            // Material/BG immer feste Position; An/Aus über die Checkboxen.
            Settings.MaterialThicknessPlacement = MaterialThicknessPlacement.BottomLeftFixed;
            Settings.BgWatermarkEnabled = BgWatermarkEnabledCheckBox.IsChecked == true;
            Settings.BgPlacement = BgPlacement.RightVerticalFixed;
            Settings.ExtractPowderCoating = ExtractPowderCoatingCheckBox.IsChecked == true;
            Settings.PowderCoatingShowRalNumber = PowderCoatingShowRalNumberCheckBox.IsChecked == true;
            Settings.ExtractAnodizing = ExtractAnodizingCheckBox.IsChecked == true;
            // Top-Band fest: Lieferort links, Auftrag Mitte, LT/FT rechts.
            Settings.DeliveryDatePosition = DeliveryDatePosition.TopRight;
            Settings.MirrorBottomToTopBand = MirrorBottomToTopBandCheckBox.IsChecked == true;

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
