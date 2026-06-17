using System.Windows;
using System.Windows.Media;

namespace PDF_umbenenen
{
    internal static class ThemeWindowService
    {
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
        }

        private static Brush GetBrush(string key)
        {
            return Application.Current?.TryFindResource(key) as Brush;
        }
    }
}
