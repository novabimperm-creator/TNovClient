using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TNovClient
{
    public class ConfigurationOption : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string DisplayName { get; set; } = string.Empty;
        public TNovConfig Config { get; set; } = new();

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
