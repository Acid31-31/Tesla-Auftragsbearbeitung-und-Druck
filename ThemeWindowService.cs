using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PDF_umbenenen
{
    internal static class ThemeWindowService
    {
        private const int DwmwaUseImmersiveDarkModeOld = 19;
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;
        private const int DwmwaBorderColor = 34;
        private const int MonitorDefaultToNearest = 2;

        private static ImageSource _appIcon;
        private static readonly Dictionary<Window, Rect> _restoreBounds = new Dictionary<Window, Rect>();
        private static readonly HashSet<Window> _workAreaMaximized = new HashSet<Window>();
        private static readonly HashSet<Window> _attachedWindows = new HashSet<Window>();

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int cbSize;
            public NativeRect rcMonitor;
            public NativeRect rcWork;
            public int dwFlags;
        }

        public static void Attach(Window window)
        {
            if (window == null || !_attachedWindows.Add(window))
                return;

            if (window.WindowStyle == WindowStyle.None)
                AttachBorderlessWindowBehavior(window);

            window.SourceInitialized += OnWindowSourceInitialized;
            window.Loaded += OnWindowLoaded;
            window.Closed += OnWindowClosed;

            if (window.IsLoaded)
                Apply(window);
        }

        private static void AttachBorderlessWindowBehavior(Window window)
        {
            window.StateChanged += OnBorderlessWindowStateChanged;
        }

        private static void OnBorderlessWindowStateChanged(object sender, EventArgs e)
        {
            if (!(sender is Window window) || window.WindowStyle != WindowStyle.None)
                return;

            if (window.WindowState == WindowState.Maximized)
            {
                window.StateChanged -= OnBorderlessWindowStateChanged;
                window.WindowState = WindowState.Normal;
                MaximizeToWorkArea(window, saveRestoreBounds: false);
                window.StateChanged += OnBorderlessWindowStateChanged;
            }
        }

        private static void OnWindowClosed(object sender, EventArgs e)
        {
            if (!(sender is Window window))
                return;

            _restoreBounds.Remove(window);
            _workAreaMaximized.Remove(window);
            _attachedWindows.Remove(window);
        }

        public static bool IsWorkAreaMaximized(Window window)
        {
            return window != null && _workAreaMaximized.Contains(window);
        }

        public static void MaximizeToWorkArea(Window window, bool saveRestoreBounds = true)
        {
            if (window == null)
                return;

            if (saveRestoreBounds && !IsWorkAreaMaximized(window))
                _restoreBounds[window] = new Rect(window.Left, window.Top, window.Width, window.Height);

            var work = GetWorkAreaForWindow(window);
            window.WindowState = WindowState.Normal;
            window.Left = work.Left;
            window.Top = work.Top;
            window.Width = work.Width;
            window.Height = work.Height;
            _workAreaMaximized.Add(window);
        }

        public static void RestoreFromWorkArea(Window window)
        {
            if (window == null)
                return;

            window.WindowState = WindowState.Normal;
            if (_restoreBounds.TryGetValue(window, out var bounds) && bounds.Width > 0 && bounds.Height > 0)
            {
                window.Left = bounds.Left;
                window.Top = bounds.Top;
                window.Width = bounds.Width;
                window.Height = bounds.Height;
            }
            else
            {
                window.Width = 1180;
                window.Height = 820;
                var work = GetWorkAreaForWindow(window);
                window.Left = work.Left + Math.Max(0, (work.Width - window.Width) / 2);
                window.Top = work.Top + Math.Max(0, (work.Height - window.Height) / 2);
            }

            _workAreaMaximized.Remove(window);
        }

        public static void ToggleWorkAreaMaximize(Window window)
        {
            if (window == null)
                return;

            if (IsWorkAreaMaximized(window))
                RestoreFromWorkArea(window);
            else
                MaximizeToWorkArea(window);
        }

        private static Rect GetWorkAreaForWindow(Window window)
        {
            try
            {
                var helper = new WindowInteropHelper(window);
                if (helper.Handle != IntPtr.Zero)
                {
                    var monitor = MonitorFromWindow(helper.Handle, MonitorDefaultToNearest);
                    if (monitor != IntPtr.Zero)
                    {
                        var info = new MonitorInfo { cbSize = Marshal.SizeOf(typeof(MonitorInfo)) };
                        if (GetMonitorInfo(monitor, ref info))
                        {
                            var source = HwndSource.FromHwnd(helper.Handle);
                            if (source?.CompositionTarget != null)
                            {
                                var m = source.CompositionTarget.TransformFromDevice;
                                var topLeft = m.Transform(new Point(info.rcWork.Left, info.rcWork.Top));
                                var bottomRight = m.Transform(new Point(info.rcWork.Right, info.rcWork.Bottom));
                                return new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback unten
            }

            return SystemParameters.WorkArea;
        }

        private static void OnWindowSourceInitialized(object sender, EventArgs e)
        {
            if (sender is Window window)
                ApplyDarkTitleBar(window);
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
            {
                Apply(window);
                ApplyDarkTitleBar(window);
            }
        }

        private static void Apply(Window window)
        {
            window.Background = GetBrush("AppBackground") ?? window.Background;
            window.Foreground = GetBrush("TextPrimary") ?? window.Foreground;
            window.Icon = GetAppIcon();
        }

        private static void ApplyDarkTitleBar(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                if (Environment.OSVersion.Version.Major < 10)
                    return;

                var useDark = 1;
                if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeOld, ref useDark, sizeof(int));

                // BGR: Hintergrund #0B1117 → 0x0017110B
                var captionColor = unchecked((int)0x0017110B);
                DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionColor, sizeof(int));

                var borderColor = unchecked((int)0x0017110B);
                DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(int));

                var textColor = unchecked((int)0x00FFFFFF);
                DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref textColor, sizeof(int));
            }
            catch
            {
                // ältere Windows-Versionen: Standard-Titelleiste bleibt
            }
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
