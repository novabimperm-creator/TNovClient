using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Win32;

namespace TNovClient
{
    public static class ThemeService
    {
        private static bool _initialized;
        private static string _appliedPath;

        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            if (Application.Current != null)
                Application.Current.Exit += (_, _) => Shutdown();
        }

        public static void Shutdown()
        {
            if (!_initialized)
                return;

            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _initialized = false;
        }

        public static bool IsWindowsLightTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key?.GetValue("AppsUseLightTheme") is int value)
                        return value != 0;
                }
            }
            catch
            {
            }

            return false;
        }

        public static string GetColorsResourcePath()
        {
            return IsWindowsLightTheme()
                ? "pack://application:,,,/TNovClient;component/Themes/Colors.Light.xaml"
                : "pack://application:,,,/TNovClient;component/Themes/Colors.Dark.xaml";
        }

        public static void Apply()
        {
            var app = Application.Current;
            if (app == null)
            {
                ApplyCore();
                return;
            }

            if (app.Dispatcher.CheckAccess())
                ApplyCore();
            else
                app.Dispatcher.Invoke(ApplyCore);
        }

        private static void ApplyCore()
        {
            string path = GetColorsResourcePath();
            if (_appliedPath == path)
                return;

            _appliedPath = path;
            ThemeColorsDictionary.ReloadAll(path);
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General
                && e.Category != UserPreferenceCategory.VisualStyle)
                return;

            Apply();
        }
    }

    public class ThemeColorsDictionary : ResourceDictionary
    {
        private static readonly List<WeakReference<ThemeColorsDictionary>> Instances = new();
        private string _loadedPath;

        public ThemeColorsDictionary()
        {
            lock (Instances)
                Instances.Add(new WeakReference<ThemeColorsDictionary>(this));

            Load(ThemeService.GetColorsResourcePath());
        }

        public static void ReloadAll(string path)
        {
            lock (Instances)
            {
                for (int i = Instances.Count - 1; i >= 0; i--)
                {
                    if (!Instances[i].TryGetTarget(out var dictionary))
                    {
                        Instances.RemoveAt(i);
                        continue;
                    }

                    dictionary.Load(path);
                }
            }
        }

        private void Load(string path)
        {
            if (_loadedPath == path && MergedDictionaries.Count > 0)
                return;

            _loadedPath = path;
            MergedDictionaries.Clear();
            MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(path, UriKind.Absolute)
            });
        }
    }
}
