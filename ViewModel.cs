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
using System.Xml.Linq;
using Newtonsoft.Json;

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

        private bool _opensInstallRequest;
        public bool OpensInstallRequest
        {
            get => _opensInstallRequest;
            set { _opensInstallRequest = value; OnPropertyChanged(); }
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

        private static readonly string[] ConfigurationDisplayNames = { "Пермь", "Хабаровск" };

        public ViewModel()
        {
            UpdCommand = new RelayCommand(_ => LoadModules(), CanUpd);
            DesktopRequestCommand = new RelayCommand(OpenDesktopInstallRequest, _ => true);
            ShowConfigurationPickerCommand = new RelayCommand(_ => OpenConfigurationPicker(), _ => true);
            CloseConfigurationPickerCommand = new RelayCommand(_ => ShowConfigurationPicker = false, _ => true);
            SelectConfigurationCommand = new RelayCommand(ApplyConfiguration, _ => true);
            files.OldVersionCheck(out string s1, out string s2, out string s3, out _, out Brush c1);
            currentversion = s1;
            actualversion = s2;
            actualcompany = s3;
            colour = c1;
            ModuleGroups = new ObservableCollection<ConfigurationGroup>();
            ReleaseEntries = new ObservableCollection<ReleaseEntry>();
            ConfigurationOptions = new ObservableCollection<ConfigurationOption>();
            LoadModules();
            LoadReleases();
        }

        private void OpenConfigurationPicker()
        {
            ConfigurationOptions.Clear();

            try
            {
                if (!File.Exists(ClientPaths.ConfigurationsFile))
                {
                    Debug.WriteLine("Файл configs.json недоступен");
                    return;
                }

                var configs = JsonConvert.DeserializeObject<List<TNovConfig>>(File.ReadAllText(ClientPaths.ConfigurationsFile));
                if (configs == null || configs.Count == 0)
                    return;

                TNovConfig? currentConfig = files.LoadConfig();

                for (int i = 0; i < configs.Count && i < ConfigurationDisplayNames.Length; i++)
                {
                    ConfigurationOptions.Add(new ConfigurationOption
                    {
                        DisplayName = ConfigurationDisplayNames[i],
                        Config = configs[i],
                        IsSelected = ConfigMatches(currentConfig, configs[i])
                    });
                }

                ShowConfigurationPicker = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки списка конфигураций: {ex}");
            }
        }

        private void ApplyConfiguration(object parameter)
        {
            if (parameter is not ConfigurationOption option)
                return;

            if (!files.SaveConfig(option.Config))
                return;

            foreach (ConfigurationOption item in ConfigurationOptions)
                item.IsSelected = ReferenceEquals(item, option);

            RefreshConfigurationInfo();
            ShowConfigurationPicker = false;
            LoadModules();
        }

        private void RefreshConfigurationInfo()
        {
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

            return string.Equals(
                ClientPaths.NormalizeServerPath(current.ServerPath),
                ClientPaths.NormalizeServerPath(candidate.ServerPath),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string GetClientVersionText()
        {
            try
            {
                string localDll = Path.Combine(ClientPaths.ClientFolder, "TNovClient.dll");
                if (File.Exists(localDll))
                    return FileVersionInfo.GetVersionInfo(localDll).ProductVersion ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";

                return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
            }
            catch
            {
                return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
            }
        }

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

                string serverPath = ClientPaths.NormalizeServerPath(config.ServerPath);
                var groupsByYear = new Dictionary<int, List<ModuleInfo>>();
                bool anyModuleLoaded = false;

                ModuleInfo desktopModule = CreateDesktopModule(serverPath);
                WindowWidth = desktopModule.OpensInstallRequest ? 450 : 380;
                ModuleGroups.Add(CreateGroup("TNovDesktop", new[] { desktopModule }));

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
                            Name = "Нет подключения",
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

        private static ModuleInfo CreateDesktopModule(string serverPath)
        {
            Version? actualAssemblyVersion = TryGetDesktopActualAssemblyVersion(serverPath);
            Version? actualClickOnceVersion = TryGetDesktopActualClickOnceVersion(serverPath);
            bool isInstalled = TryGetDesktopInstalledVersion(out Version? currentVersion, out bool usesClickOnceVersion);
            Version? actualVersion = usesClickOnceVersion && actualClickOnceVersion != null
                ? actualClickOnceVersion
                : actualAssemblyVersion ?? actualClickOnceVersion;
            bool isActual = isInstalled
                && actualVersion != null
                && currentVersion != null
                && currentVersion.CompareTo(actualVersion) >= 0;

            var module = new ModuleInfo
            {
                Name = "TNovDesktop",
                CurrentVersion = isInstalled ? FormatVersion(currentVersion) : "не установлен",
                ActualVersion = FormatVersion(actualVersion),
                IsActual = isActual || (isInstalled && actualVersion == null),
                IsDesktopActionVisible = !isInstalled || !isActual
            };

            if (!isInstalled)
            {
                module.DesktopActionText = "Заявка на установку";
                module.DesktopActionWidth = 130;
                module.OpensInstallRequest = true;
            }
            else if (!isActual)
            {
                module.DesktopActionText = "?";
                module.DesktopActionWidth = 26;
                module.DesktopActionToolTip = "Для обновления закройте программу через трей и откройте снова через меню Пуск";
            }

            return module;
        }

        private static Version? TryGetDesktopActualAssemblyVersion(string serverPath)
        {
            string desktopFolder = Path.Combine(serverPath, "actual", "desktop");
            string versionFile = Path.Combine(desktopFolder, "version.txt");

            try
            {
                if (File.Exists(versionFile) && Version.TryParse(File.ReadAllText(versionFile).Trim(), out Version? version))
                    return version;

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovDesktop assembly version read failed: {ex.Message}");
                return null;
            }
        }

        private static Version? TryGetDesktopActualClickOnceVersion(string serverPath)
        {
            string desktopFolder = Path.Combine(serverPath, "actual", "desktop");

            try
            {
                string manifestFile = Path.Combine(desktopFolder, "TNovDesktop.application");
                if (!File.Exists(manifestFile))
                    return null;

                return TryReadClickOnceManifestVersion(manifestFile);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovDesktop ClickOnce version read failed: {ex.Message}");
                return null;
            }
        }

        private static bool TryGetDesktopInstalledVersion(out Version? version, out bool usesClickOnceVersion)
        {
            version = null;
            usesClickOnceVersion = false;

            if (!DesktopShortcutExists())
                return false;

            foreach (string exePath in GetDesktopExecutableCandidates())
            {
                try
                {
                    Version? assemblyVersion = AssemblyName.GetAssemblyName(exePath).Version;
                    if (assemblyVersion != null)
                    {
                        version = assemblyVersion;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TNovDesktop version read failed ({exePath}): {ex.Message}");
                }
            }

            foreach (string manifestPath in GetDesktopManifestCandidates())
            {
                Version? manifestVersion = TryReadClickOnceManifestVersion(manifestPath);
                if (manifestVersion != null)
                {
                    version = manifestVersion;
                    usesClickOnceVersion = true;
                    return true;
                }
            }

            return true;
        }

        private static bool DesktopShortcutExists()
        {
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string[] shortcuts =
            {
                Path.Combine(programs, "TNov", "TNovDesktop.appref-ms"),
                Path.Combine(programs, "TNovDesktop.appref-ms"),
                Path.Combine(programs, "TNovDesktop", "TNovDesktop.appref-ms")
            };

            return shortcuts.Any(File.Exists);
        }

        private static IEnumerable<string> GetDesktopExecutableCandidates()
        {
            string clickOnceCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Apps",
                "2.0");

            if (!Directory.Exists(clickOnceCache))
                return Enumerable.Empty<string>();

            try
            {
                return Directory.EnumerateFiles(clickOnceCache, "TNovDesktop.*", SearchOption.AllDirectories)
                    .Where(path =>
                        path.EndsWith("TNovDesktop.dll", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith("TNovDesktop.exe", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovDesktop ClickOnce cache scan failed: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }

        private static IEnumerable<string> GetDesktopManifestCandidates()
        {
            string clickOnceCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Apps",
                "2.0");

            if (!Directory.Exists(clickOnceCache))
                return Enumerable.Empty<string>();

            try
            {
                return Directory.EnumerateFiles(clickOnceCache, "TNovDesktop.exe.manifest", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovDesktop manifest scan failed: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }

        private static Version? TryReadClickOnceManifestVersion(string manifestFile)
        {
            try
            {
                XNamespace ns = "urn:schemas-microsoft-com:asm.v1";
                string? manifestVersion = XDocument.Load(manifestFile)
                    .Root?
                    .Element(ns + "assemblyIdentity")?
                    .Attribute("version")?
                    .Value;

                return Version.TryParse(manifestVersion, out Version? version) ? version : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ClickOnce manifest version read failed ({manifestFile}): {ex.Message}");
                return null;
            }
        }

        private static string FormatVersion(Version? version) => version?.ToString() ?? "?";

        private static void OpenDesktopInstallRequest(object parameter)
        {
            if (parameter is ModuleInfo module && !module.OpensInstallRequest)
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = "https://tnov.pm-nova.ru/?tab=requests",
                UseShellExecute = true
            });
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
                string releasesFile = ClientPaths.ReleasesFile;
                if (!File.Exists(releasesFile))
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
