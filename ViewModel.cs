using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace TNovClient
{
    public class ConfigurationGroup : INotifyPropertyChanged
    {
        private string _title;
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        private ObservableCollection<ModuleInfo> _modules;
        public ObservableCollection<ModuleInfo> Modules
        {
            get => _modules;
            set { _modules = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ModuleInfo : INotifyPropertyChanged
    {
        private string _name;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private string _currentVersion;
        public string CurrentVersion
        {
            get => _currentVersion;
            set { _currentVersion = value; OnPropertyChanged(); }
        }

        private string _actualVersion;
        public string ActualVersion
        {
            get => _actualVersion;
            set { _actualVersion = value; OnPropertyChanged(); }
        }

        private bool _isActual;
        public bool IsActual
        {
            get => _isActual;
            set { _isActual = value; OnPropertyChanged(); }
        }

        private bool _isDesktopActionVisible;
        public bool IsDesktopActionVisible
        {
            get => _isDesktopActionVisible;
            set { _isDesktopActionVisible = value; OnPropertyChanged(); }
        }

        private string _desktopActionText = string.Empty;
        public string DesktopActionText
        {
            get => _desktopActionText;
            set { _desktopActionText = value; OnPropertyChanged(); }
        }

        private string _desktopActionToolTip = string.Empty;
        public string DesktopActionToolTip
        {
            get => _desktopActionToolTip;
            set { _desktopActionToolTip = value; OnPropertyChanged(); }
        }

        private double _desktopActionWidth = 26;
        public double DesktopActionWidth
        {
            get => _desktopActionWidth;
            set { _desktopActionWidth = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ViewModel : INotifyPropertyChanged
    {
        private Brush _colour = new SolidColorBrush(Colors.DarkSeaGreen);
        public Brush colour { get => _colour; set { _colour = value; OnPropertyChanged(nameof(colour)); } }

        private string _currentversion;
        public string currentversion { get => _currentversion; set { _currentversion = value; OnPropertyChanged(nameof(currentversion)); } }
        private string _actualversion;
        public string actualversion { get => _actualversion; set { _actualversion = value; OnPropertyChanged(nameof(actualversion)); } }
        private string _actualcompany;
        public string actualcompany { get => _actualcompany; set { _actualcompany = value; OnPropertyChanged(nameof(actualcompany)); } }

        private string _TNovClientVersion = "TNov Клиент " + GetClientVersionText();
        public string TNovClientVersion { get => _TNovClientVersion; set { _TNovClientVersion = value; OnPropertyChanged(nameof(TNovClientVersion)); } }

        private double _windowWidth = 380;
        public double WindowWidth
        {
            get => _windowWidth;
            set { _windowWidth = value; OnPropertyChanged(nameof(WindowWidth)); }
        }

        private ObservableCollection<ConfigurationGroup> _moduleGroups;
        public ObservableCollection<ConfigurationGroup> ModuleGroups
        {
            get => _moduleGroups;
            set { _moduleGroups = value; OnPropertyChanged(nameof(ModuleGroups)); }
        }

        private ObservableCollection<ReleaseEntry> _releaseEntries;
        public ObservableCollection<ReleaseEntry> ReleaseEntries
        {
            get => _releaseEntries;
            set { _releaseEntries = value; OnPropertyChanged(nameof(ReleaseEntries)); OnPropertyChanged(nameof(HasReleaseEntries)); }
        }

        private ReleaseEntry _selectedRelease;
        public ReleaseEntry SelectedRelease
        {
            get => _selectedRelease;
            set { _selectedRelease = value; OnPropertyChanged(nameof(SelectedRelease)); }
        }

        private string _journalStatusMessage = string.Empty;
        public string JournalStatusMessage
        {
            get => _journalStatusMessage;
            set { _journalStatusMessage = value; OnPropertyChanged(nameof(JournalStatusMessage)); OnPropertyChanged(nameof(HasReleaseEntries)); }
        }

        public bool HasReleaseEntries => ReleaseEntries.Count > 0 && string.IsNullOrEmpty(JournalStatusMessage);

        private bool _showConfigurationPicker;
        public bool ShowConfigurationPicker
        {
            get => _showConfigurationPicker;
            set { _showConfigurationPicker = value; OnPropertyChanged(nameof(ShowConfigurationPicker)); }
        }

        private ObservableCollection<ConfigurationOption> _configurationOptions;
        public ObservableCollection<ConfigurationOption> ConfigurationOptions
        {
            get => _configurationOptions;
            set { _configurationOptions = value; OnPropertyChanged(nameof(ConfigurationOptions)); }
        }

        public RelayCommand UpdCommand { get; set; }
        public ICommand DesktopRequestCommand { get; set; }
        public ICommand ShowConfigurationPickerCommand { get; set; }
        public ICommand CloseConfigurationPickerCommand { get; set; }
        public ICommand SelectConfigurationCommand { get; set; }
        public ICommand CollectLogsCommand { get; set; }

        private bool _isPersonalConfig;
        /// <summary>Личная конфигурация: логи остаются на ПК, видна кнопка «Собрать логи».</summary>
        public bool IsPersonalConfig
        {
            get => _isPersonalConfig;
            set { _isPersonalConfig = value; OnPropertyChanged(nameof(IsPersonalConfig)); }
        }

        public ViewModel()
        {
            UpdCommand = new RelayCommand(_ => LoadModules(), CanUpd);
            DesktopRequestCommand = new RelayCommand(OpenDesktopInstallRequest, _ => true);
            ShowConfigurationPickerCommand = new RelayCommand(_ => OpenConfigurationPicker(), _ => true);
            CloseConfigurationPickerCommand = new RelayCommand(_ => ShowConfigurationPicker = false, _ => true);
            SelectConfigurationCommand = new RelayCommand(ApplyConfiguration, _ => true);
            CollectLogsCommand = new RelayCommand(_ => LogCollector.CollectAndShow(), _ => true);
            IsPersonalConfig = OfficeServers.IsPersonal(files.LoadConfig());
            files.OldVersionCheck(out string s1, out string s2, out string s3, out _, out Brush c1);
            currentversion = s1;
            actualversion = s2;
            actualcompany = s3;
            colour = c1;
            ModuleGroups = new ObservableCollection<ConfigurationGroup>();
            ReleaseEntries = new ObservableCollection<ReleaseEntry>();
            ConfigurationOptions = new ObservableCollection<ConfigurationOption>();
            LoadModules();
        }

        private void OpenConfigurationPicker()
        {
            ConfigurationOptions.Clear();

            TNovConfig? currentConfig = files.LoadConfig();
            foreach ((string displayName, TNovConfig config) in OfficeServers.GetAll())
            {
                ConfigurationOptions.Add(new ConfigurationOption
                {
                    DisplayName = displayName,
                    Config = config,
                    IsSelected = ConfigMatches(currentConfig, config)
                });
            }

            ShowConfigurationPicker = true;
        }

        private void ApplyConfiguration(object parameter)
        {
            if (parameter is not ConfigurationOption option)
                return;

            TNovConfig? previous = files.LoadConfig();
            if (!files.SaveConfig(option.Config))
                return;

            ServerProbe.Invalidate(previous?.ServerPath);
            ServerProbe.Invalidate(option.Config.ServerPath);

            foreach (ConfigurationOption item in ConfigurationOptions)
                item.IsSelected = ReferenceEquals(item, option);

            RefreshConfigurationInfo();
            ShowConfigurationPicker = false;
            LoadModules();
            _ = SyncMirrorAfterSwitchAsync();
        }

        /// <summary>Личная конфигурация: сразу скачать зеркало, не дожидаясь цикла обновлений.</summary>
        private async Task SyncMirrorAfterSwitchAsync()
        {
            TNovConfig? config = files.LoadConfig();
            if (config?.UpdateSource?.IsGoogleDrive != true)
                return;
            await GoogleDriveMirror.SyncIfDueAsync(config, force: true);
            RefreshConfigurationInfo();
            LoadModules();
            LoadReleases();
        }

        private void RefreshConfigurationInfo()
        {
            IsPersonalConfig = OfficeServers.IsPersonal(files.LoadConfig());
            files.OldVersionCheck(out string s1, out string s2, out string s3, out _, out Brush c1);
            currentversion = s1;
            actualversion = s2;
            actualcompany = s3;
            colour = c1;
        }

        private static bool ConfigMatches(TNovConfig? current, TNovConfig? candidate)
        {
            if (current == null || candidate == null)
                return false;

            if (OfficeServers.IsPersonal(current) || OfficeServers.IsPersonal(candidate))
                return OfficeServers.IsPersonal(current) && OfficeServers.IsPersonal(candidate);

            return OfficeServers.PathsMatch(current.ServerPath, candidate.ServerPath);
        }

        private static string GetClientVersionText()
        {
            try
            {
                string localDll = Path.Combine(ClientPaths.ClientFolder, "TNovClient.dll");
                if (File.Exists(localDll))
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(localDll);
                    if (TryFormatThreePartVersion(info.ProductVersion, out string fromProduct)
                        || TryFormatThreePartVersion(info.FileVersion, out fromProduct))
                        return fromProduct;
                }

                return FormatThreePartVersion(Assembly.GetExecutingAssembly().GetName().Version);
            }
            catch
            {
                return FormatThreePartVersion(Assembly.GetExecutingAssembly().GetName().Version);
            }
        }

        private static bool TryFormatThreePartVersion(string? value, out string formatted)
        {
            formatted = "?";
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string numeric = value.Split('+', '-')[0].Trim();
            if (!Version.TryParse(numeric, out Version? version) || version == null)
                return false;

            formatted = FormatThreePartVersion(version);
            return true;
        }

        private static string FormatThreePartVersion(Version? version) =>
            version == null ? "?" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

        private void LoadModules()
        {
            ModuleGroups.Clear();
            try
            {
                TNovConfig config = files.LoadConfig();
                if (config == null)
                {
                    ModuleGroups.Add(CreateGroup("Статус", new[]
                    {
                        new ModuleInfo { Name = "Конфигурация недоступна", IsActual = false }
                    }));
                    return;
                }

                if (!servercheck.ServerCheck())
                {
                    ModuleGroups.Add(CreateGroup("Статус", new[]
                    {
                        new ModuleInfo
                        {
                            Name = NoModulesText(config),
                            CurrentVersion = "",
                            ActualVersion = "",
                            IsActual = false
                        }
                    }));
                    return;
                }

                string serverPath = ClientPaths.UpdateRoot(config);
                var groupsByYear = new Dictionary<int, List<ModuleInfo>>();
                bool anyModuleLoaded = false;

                // TNovDesktop ставится только с корпоративной шары (ClickOnce), в личную поставку не входит.
                if (config.IsCorp)
                {
                    ModuleInfo desktopModule = CreateDesktopModule(serverPath);
                    WindowWidth = desktopModule.IsDesktopActionVisible ? 410 : 380;
                    ModuleGroups.Add(CreateGroup("TNovDesktop", new[] { desktopModule }));
                }
                else
                {
                    WindowWidth = 380;
                }

                foreach (int revitYear in RevitInstallHelper.GetDeployTargetYears(serverPath))
                {
                    if (!RevitInstallHelper.TryGetServerPluginFolder(serverPath, revitYear, out string serverPluginFolder))
                        continue;

                    if (!Directory.Exists(serverPluginFolder))
                        continue;

                    if (!groupsByYear.ContainsKey(revitYear))
                        groupsByYear[revitYear] = new List<ModuleInfo>();

                    foreach (string file in Directory.GetFiles(serverPluginFolder).Select(Path.GetFileName))
                    {
                        if (string.IsNullOrEmpty(file) || file.Contains("addin", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string serverFile = Path.Combine(serverPluginFolder, file);
                        FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(serverFile);
                        string name = FormatModuleName(fvi.ProductName ?? Path.GetFileNameWithoutExtension(file));
                        string actver = fvi.ProductVersion ?? "?";

                        if (IsHiddenModule(name, file))
                            continue;

                        string localFile = Path.Combine(ClientPaths.LocalPluginFolder(revitYear), file);
                        string curver = File.Exists(localFile)
                            ? FileVersionInfo.GetVersionInfo(localFile).ProductVersion ?? "?"
                            : "не установлен";

                        // Сравнение по ProductVersion (семантически), как в синхронизации dll.
                        bool isActual = files.IsProductVersionActual(curver, actver);

                        groupsByYear[revitYear].Add(new ModuleInfo
                        {
                            Name = name,
                            CurrentVersion = curver,
                            ActualVersion = actver,
                            IsActual = isActual
                        });
                        anyModuleLoaded = true;
                    }
                }

                if (!anyModuleLoaded)
                {
                    ModuleGroups.Add(CreateGroup("Статус", new[]
                    {
                        new ModuleInfo
                        {
                            Name = NoModulesText(config),
                            CurrentVersion = "",
                            ActualVersion = "",
                            IsActual = false
                        }
                    }));
                    return;
                }

                foreach (int year in groupsByYear.Keys.OrderBy(y => y))
                {
                    if (groupsByYear[year].Count == 0)
                        continue;

                    var sortedModules = SortModules(groupsByYear[year]);

                    ModuleGroups.Add(CreateGroup($"TNov для Revit {year}", sortedModules));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки модулей: {ex}");
            }
        }

        private static ConfigurationGroup CreateGroup(string title, IEnumerable<ModuleInfo> modules) =>
            new ConfigurationGroup
            {
                Title = title,
                Modules = new ObservableCollection<ModuleInfo>(modules)
            };

        /// <summary>Почему нечего показать: для личной конфигурации — понятнее, чем «Нет подключения».</summary>
        private static string NoModulesText(TNovConfig config)
        {
            if (config.IsCorp)
                return "Нет подключения";
            if (config.UpdateSource?.IsGoogleDrive != true)
                return "Не задан источник обновлений";
            return GoogleDriveMirror.HasContent ? "На Google Drive нет плагина" : "Загрузка с Google Drive…";
        }

        private static ModuleInfo CreateDesktopModule(string serverPath)
        {
            DesktopStatus status = DesktopUpdateService.GetStatus(serverPath);

            var module = new ModuleInfo
            {
                Name = "TNovDesktop",
                CurrentVersion = status.IsInstalled ? DesktopUpdateService.FormatVersion(status.CurrentVersion) : "не установлен",
                ActualVersion = DesktopUpdateService.FormatVersion(status.ActualVersion),
                IsActual = status.IsActual || (status.IsInstalled && status.ActualVersion == null),
                IsDesktopActionVisible = !status.IsInstalled || !status.IsActual
            };

            if (!status.IsInstalled)
            {
                module.DesktopActionText = "Установить";
                module.DesktopActionWidth = 90;
                module.DesktopActionToolTip = "Установит TNovDesktop через ClickOnce";
            }
            else if (!status.IsActual)
            {
                module.DesktopActionText = "Обновить";
                module.DesktopActionWidth = 90;
                module.DesktopActionToolTip = "Запустит установщик ClickOnce. Если TNovDesktop открыт — сначала закройте его через трей";
            }

            return module;
        }

        private static void OpenDesktopInstallRequest(object parameter)
        {
            _ = DesktopUpdateService.UpdateFromUiAsync();
        }

        private static string FormatModuleName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            const string prefix = "TNov. ";
            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? name.Substring(prefix.Length)
                : name;
        }

        private static bool IsHiddenModule(string name, string fileName)
        {
            string combined = $"{name} {fileName}".ToLowerInvariant();
            return combined.Contains("microsoft") || combined.Contains("npgsql");
        }

        private static int GetModulePriority(string name)
        {
            if (name.Contains("Оболочка", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (name.Contains("Общие настройки", StringComparison.OrdinalIgnoreCase))
                return 1;
            return 2;
        }

        private static List<ModuleInfo> SortModules(List<ModuleInfo> modules)
        {
            bool allActual = modules.All(m => m.IsActual);

            if (allActual)
            {
                return modules
                    .OrderBy(m => GetModulePriority(m.Name))
                    .ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }

            return modules
                .OrderBy(m => m.IsActual)
                .ThenBy(m => GetModulePriority(m.Name))
                .ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public void LoadReleases()
        {
            ReleaseEntries.Clear();
            JournalStatusMessage = string.Empty;

            try
            {
                string releasesFile = ClientPaths.ReleasesFile(files.LoadConfig());
                if (!ServerProbe.IsPathReachable(releasesFile))
                {
                    JournalStatusMessage = "Журнал версий недоступен";
                    OnPropertyChanged(nameof(HasReleaseEntries));
                    return;
                }

                foreach (ReleaseEntry entry in ReleaseLogParser.ParseFile(releasesFile))
                    ReleaseEntries.Add(entry);

                if (ReleaseEntries.Count == 0)
                {
                    JournalStatusMessage = "Журнал версий пуст";
                    OnPropertyChanged(nameof(HasReleaseEntries));
                    return;
                }

                SelectedRelease = ReleaseEntries[0];
                OnPropertyChanged(nameof(HasReleaseEntries));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки журнала версий: {ex}");
                JournalStatusMessage = "Ошибка загрузки журнала версий";
                OnPropertyChanged(nameof(HasReleaseEntries));
            }
        }

        public void Upd()
        {
            new logaddtext(DateTime.Now, "Обновить сейчас");
            files.Updater();
            files.OldVersionCheck(out string s1, out string s2, out string s3, out _, out Brush c1);
            currentversion = s1;
            actualversion = s2;
            actualcompany = s3;
            colour = c1;
            LoadModules();
            LoadReleases();
        }

        private bool CanUpd(object param) => servercheck.ServerCheck();

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
