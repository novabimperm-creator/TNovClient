using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace TNovClient
{
    public class ChangeEntry
    {
        public string TypeLabel { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    public class ReleaseEntry : INotifyPropertyChanged
    {
        private string _moduleName = string.Empty;
        public string ModuleName
        {
            get => _moduleName;
            set { _moduleName = value; OnPropertyChanged(); }
        }

        private string _version = string.Empty;
        public string Version
        {
            get => _version;
            set { _version = value; OnPropertyChanged(); }
        }

        private string _releaseDate = string.Empty;
        public string ReleaseDate
        {
            get => _releaseDate;
            set { _releaseDate = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ChangeEntry> Changes { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal static class ReleaseLogParser
    {
        private static readonly (string Prefix, string Label)[] ChangeTypes =
        {
            ("imp", "Улучшение"),
            ("fix", "Исправление"),
            ("new", "Новое")
        };

        public static List<ReleaseEntry> ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
                return new List<ReleaseEntry>();

            var entries = new List<ReleaseEntry>();
            foreach (string line in File.ReadLines(filePath))
            {
                if (TryParseLine(line, out ReleaseEntry? entry) && entry != null)
                    entries.Add(entry);
            }

            entries.Reverse();
            return entries;
        }

        public static bool TryParseLine(string line, out ReleaseEntry? entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(line))
                return false;

            string[] parts = line.Split('/');
            if (parts.Length < 4)
                return false;

            string changesRaw = string.Join("/", parts.Skip(3));
            var changes = ParseChanges(changesRaw);

            entry = new ReleaseEntry
            {
                ModuleName = parts[0].Trim(),
                Version = parts[1].Trim(),
                ReleaseDate = parts[2].Trim(),
                Changes = new ObservableCollection<ChangeEntry>(changes)
            };
            return true;
        }

        private static List<ChangeEntry> ParseChanges(string changesRaw)
        {
            if (string.IsNullOrWhiteSpace(changesRaw))
                return new List<ChangeEntry>();

            return SplitChangeSegments(changesRaw)
                .Select(ParseChangeSegment)
                .ToList();
        }

        private static List<string> SplitChangeSegments(string changesRaw)
        {
            string[] parts = changesRaw.Split('/');
            var segments = new List<string>();
            var current = new StringBuilder();

            foreach (string part in parts)
            {
                if (current.Length > 0 && IsNewChangeSegment(part))
                {
                    segments.Add(current.ToString());
                    current.Clear();
                }

                if (current.Length > 0)
                    current.Append('/');
                current.Append(part);
            }

            if (current.Length > 0)
                segments.Add(current.ToString());

            return segments;
        }

        private static bool IsNewChangeSegment(string part)
        {
            string trimmed = part.TrimStart();
            return trimmed.StartsWith("imp ", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("fix ", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("new ", StringComparison.OrdinalIgnoreCase);
        }

        private static ChangeEntry ParseChangeSegment(string segment)
        {
            segment = segment.Trim();
            foreach ((string prefix, string label) in ChangeTypes)
            {
                if (segment.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                {
                    return new ChangeEntry
                    {
                        TypeLabel = label,
                        Text = segment.Substring(prefix.Length + 1).Trim()
                    };
                }
            }

            return new ChangeEntry
            {
                TypeLabel = "Улучшение",
                Text = segment
            };
        }
    }
}
