#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace ZiloreMiniMap {
    /// <summary>
    /// Zero-dependency MVVM base class for bindable ViewModels (null-safe).
    /// </summary>
    internal abstract class ObservableObject : INotifyPropertyChanged {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null) {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    internal sealed class RelayCommand : ICommand {
        readonly Action execute;
        readonly Func<bool>? canExecute;

        internal RelayCommand(Action execute, Func<bool>? canExecute = null) {
            ArgumentNullException.ThrowIfNull(execute);
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) {
            return canExecute == null || canExecute();
        }

        public void Execute(object? parameter) {
            if (CanExecute(parameter)) execute();
        }

        internal void RaiseCanExecuteChanged() {
            var handler = CanExecuteChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }

    internal sealed class LogEntryViewModel {
        public string TimeText { get; set; } = "";
        public string LevelShort { get; set; } = "";
        public string SourceTag { get; set; } = "";
        public string Message { get; set; } = "";
        public string FormattedLine { get; set; } = "";
        public string ToolTipText { get; set; } = "";
        public Brush LevelBrush { get; set; } = Brushes.Transparent;
        public Brush MessageBrush { get; set; } = Brushes.Transparent;
    }

    /// <summary>
    /// MVVM ViewModel for the real-time diagnostic Journal / Logs console.
    /// </summary>
    internal sealed class LogConsoleViewModel : ObservableObject {
        readonly bool isRussian;
        readonly string allModulesLabel;
        readonly string allLevelPrefix;
        readonly string entryToolTip;

        string activeLevel = "ALL";
        string activeModule = "";
        string searchQuery = "";
        bool isPaused;
        bool autoScroll = true;
        string countSummary = "";
        string allLabel = "ALL (0)";
        string infoLabel = "INFO (0)";
        string warnLabel = "Warning (0)";
        string errorLabel = "ERROR (0)";
        bool isEmpty = true;
        long lastRevision = -1;

        public ObservableCollection<LogEntryViewModel> Entries { get; private set; }
        public ObservableCollection<string> Modules { get; private set; }

        internal LogConsoleViewModel(bool russian, string? allLevelText) {
            isRussian = russian;
            allModulesLabel = russian ? "Все модули" : "All modules";
            allLevelPrefix = string.IsNullOrWhiteSpace(allLevelText) ? (russian ? "Все" : "All") : allLevelText;
            entryToolTip = russian ? "Двойной клик — скопировать строку" : "Double-click to copy entry";
            Entries = new ObservableCollection<LogEntryViewModel>();
            Modules = new ObservableCollection<string> { allModulesLabel };
            ClearCommand = new RelayCommand(() => { AppLogBuffer.Clear(); Refresh(true); });
        }

        public RelayCommand ClearCommand { get; private set; }
        public string AllModulesLabel { get { return allModulesLabel; } }

        public string ActiveLevel {
            get { return activeLevel; }
            set {
                string next = string.IsNullOrWhiteSpace(value) ? "ALL" : value.Trim().ToUpperInvariant();
                if (SetProperty(ref activeLevel, next)) Refresh(true);
            }
        }

        public string ActiveModule {
            get { return activeModule; }
            set {
                string next = (string.IsNullOrWhiteSpace(value) || value == allModulesLabel) ? "" : value.Trim();
                if (SetProperty(ref activeModule, next)) Refresh(true);
            }
        }

        public string SearchQuery {
            get { return searchQuery; }
            set {
                string next = value ?? "";
                if (SetProperty(ref searchQuery, next)) Refresh(true);
            }
        }

        public bool IsPaused {
            get { return isPaused; }
            set {
                if (SetProperty(ref isPaused, value) && !value) Refresh(true);
            }
        }

        public bool AutoScroll {
            get { return autoScroll; }
            set { SetProperty(ref autoScroll, value); }
        }

        public string CountSummary {
            get { return countSummary; }
            private set { SetProperty(ref countSummary, value); }
        }

        public string AllLabel {
            get { return allLabel; }
            private set { SetProperty(ref allLabel, value); }
        }

        public string InfoLabel {
            get { return infoLabel; }
            private set { SetProperty(ref infoLabel, value); }
        }

        public string WarnLabel {
            get { return warnLabel; }
            private set { SetProperty(ref warnLabel, value); }
        }

        public string ErrorLabel {
            get { return errorLabel; }
            private set { SetProperty(ref errorLabel, value); }
        }

        public bool IsEmpty {
            get { return isEmpty; }
            private set { SetProperty(ref isEmpty, value); }
        }

        internal bool Refresh(bool force = false) {
            if (isPaused && !force && lastRevision >= 0) return false;
            long rev = AppLogBuffer.Revision;
            if (!force && rev == lastRevision) return false;
            lastRevision = rev;

            int cTotal, cInfo, cWarn, cErr;
            AppLogBuffer.GetLevelCounts(out cTotal, out cInfo, out cWarn, out cErr);
            AllLabel = allLevelPrefix + " (" + cTotal + ")";
            InfoLabel = "INFO (" + cInfo + ")";
            WarnLabel = "Warning (" + cWarn + ")";
            ErrorLabel = "ERROR (" + cErr + ")";

            var srcNames = AppLogBuffer.GetDistinctSources();
            if (Modules.Count != srcNames.Length + 1) {
                Modules.Clear();
                Modules.Add(allModulesLabel);
                for (int i = 0; i < srcNames.Length; i++) Modules.Add(srcNames[i]);
            }

            var rawEntries = AppLogBuffer.Snapshot(activeLevel);
            string q = (searchQuery ?? "").Trim();
            Entries.Clear();

            Brush inkBrush = WpfFieldTheme.Ink;
            Brush dangerBrush = WpfFieldTheme.Danger;
            Brush amberBrush = WpfFieldTheme.Amber;
            Brush cyanBrush = WpfFieldTheme.Cyan;

            for (int i = 0; i < rawEntries.Length; i++) {
                var e = rawEntries[i];
                if (!string.IsNullOrEmpty(activeModule) && !string.Equals(e.Source, activeModule, StringComparison.OrdinalIgnoreCase)) continue;
                string formatted = e.FormatLine();
                if (q.Length > 0 && formatted.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;

                bool isErr = string.Equals(e.Level, "ERROR", StringComparison.OrdinalIgnoreCase);
                bool isWarn = string.Equals(e.Level, "WARNING", StringComparison.OrdinalIgnoreCase);
                Entries.Add(new LogEntryViewModel {
                    TimeText = e.TimestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    LevelShort = isErr ? "ERR" : (isWarn ? "WARN" : "INFO"),
                    SourceTag = "[" + e.Source + "]",
                    Message = e.Message ?? "",
                    FormattedLine = formatted,
                    ToolTipText = entryToolTip,
                    LevelBrush = isErr ? dangerBrush : (isWarn ? amberBrush : cyanBrush),
                    MessageBrush = isErr ? dangerBrush : inkBrush
                });
            }

            IsEmpty = Entries.Count == 0;
            CountSummary = (isRussian ? "Показано: " : "Shown: ") + Entries.Count + " / " + cTotal;
            return true;
        }

        internal string ExportVisibleText(string? emptyFallback) {
            if (Entries.Count == 0) return emptyFallback ?? "";
            var sb = new StringBuilder(Entries.Count * 96);
            for (int i = 0; i < Entries.Count; i++) {
                if (i > 0) sb.AppendLine();
                sb.Append(Entries[i].FormattedLine);
            }
            return sb.ToString();
        }

        static DataTemplate? cachedItemTemplate;
        internal static DataTemplate CreateItemTemplate() {
            if (cachedItemTemplate == null) {
                const string xaml = @"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
<DockPanel ToolTip='{Binding ToolTipText}' Margin='0,1' TextOptions.TextRenderingMode='ClearType' TextOptions.TextFormattingMode='Display' RenderOptions.ClearTypeHint='Enabled'>
  <TextBlock DockPanel.Dock='Left' Text='{Binding TimeText}' FontFamily='Segoe UI Variable, Segoe UI Variable Text, Segoe UI' Typography.NumeralAlignment='Tabular' FontSize='11.5' Foreground='{DynamicResource ThemeMuted}' VerticalAlignment='Top' Margin='0,2,8,0'/>
  <Border DockPanel.Dock='Left' Background='{DynamicResource ThemeRaised}' BorderBrush='{DynamicResource ThemeLine}' BorderThickness='1' CornerRadius='6' Padding='6,1' MinWidth='46' VerticalAlignment='Top' Margin='0,0,8,0'>
    <TextBlock Text='{Binding LevelShort}' FontSize='10.5' FontWeight='SemiBold' Foreground='{Binding LevelBrush}' HorizontalAlignment='Center'/>
  </Border>
  <TextBlock DockPanel.Dock='Left' Text='{Binding SourceTag}' FontFamily='Segoe UI Variable, Segoe UI Variable Text, Segoe UI' FontSize='11.5' FontWeight='SemiBold' Foreground='{DynamicResource ThemeInk}' VerticalAlignment='Top' Margin='0,2,8,0'/>
  <TextBlock Text='{Binding Message}' FontFamily='Segoe UI Variable, Segoe UI Variable Text, Segoe UI' FontSize='12' Foreground='{Binding MessageBrush}' TextWrapping='Wrap' VerticalAlignment='Top' Margin='0,1,0,0'/>
</DockPanel>
</DataTemplate>";
                cachedItemTemplate = (DataTemplate)XamlReader.Parse(xaml);
            }
            return cachedItemTemplate;
        }
    }
}
