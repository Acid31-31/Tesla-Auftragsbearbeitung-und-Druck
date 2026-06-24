using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PDF_umbenenen
{
    internal static class ThemeWindowService
    {
        private static ImageSource _appIcon;

        public static void Attach(Window window)
        {
            if (window == null)
                return;

            window.Loaded += OnWindowLoaded;
            if (window.IsLoaded)
                Apply(window);
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
                Apply(window);
        }

        public static void RefreshAllWindows()
        {
            if (Application.Current == null)
                return;

            foreach (Window window in Application.Current.Windows)
                Apply(window);
        }

        private static void Apply(Window window)
        {
            window.Background = GetBrush("AppBackground") ?? window.Background;
            window.Foreground = GetBrush("TextPrimary") ?? window.Foreground;
            window.Icon = GetAppIcon();
        }

        public static ImageSource GetAppIcon()
        {
            if (_appIcon != null)
                return _appIcon;

            try
            {
                var iconPath = AppInfo.GetIconPath();
                if (!string.IsNullOrWhiteSpace(iconPath))
                {
                    using (var stream = File.OpenRead(iconPath))
                    {
                        _appIcon = BitmapFrame.Create(
                            stream,
                            BitmapCreateOptions.None,
                            BitmapCacheOption.OnLoad);
                        _appIcon.Freeze();
                        return _appIcon;
                    }
                }
            }
            catch
            {
                _appIcon = null;
            }

            return _appIcon;
        }

        private static Brush GetBrush(string key)
        {
            return Application.Current?.TryFindResource(key) as Brush;
        }
    }
}
