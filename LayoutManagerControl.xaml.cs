using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TypeFix.Models;

namespace TypeFix
{
    public partial class LayoutManagerControl : System.Windows.Controls.UserControl
    {
        public static readonly DependencyProperty ShowManagementProperty =
            DependencyProperty.Register(
                nameof(ShowManagement),
                typeof(bool),
                typeof(LayoutManagerControl),
                new PropertyMetadata(true, OnShowManagementChanged));

        private LayoutCatalog? _catalog;
        private bool _refreshing;

        public LayoutManagerControl()
        {
            InitializeComponent();
            ApplyManagementVisibility();
        }

        public bool ShowManagement
        {
            get => (bool)GetValue(ShowManagementProperty);
            set => SetValue(ShowManagementProperty, value);
        }

        public Action<string, bool>? ReportStatus { get; set; }

        public void Attach(LayoutCatalog catalog)
        {
            if (_catalog != null)
                _catalog.Changed -= OnCatalogChanged;

            _catalog = catalog;
            _catalog.Changed += OnCatalogChanged;
            Refresh();
        }

        public void Refresh()
        {
            if (_catalog == null)
                return;

            _refreshing = true;
            try
            {
                LayoutCombo.ItemsSource = _catalog.Profiles
                    .Select(profile => new LayoutChoice(_catalog.Label(profile), profile.Id))
                    .ToList();
                LayoutCombo.SelectedValue = _catalog.ActiveId;
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void OnCatalogChanged()
        {
            Dispatcher.BeginInvoke(Refresh);
        }

        private static void OnShowManagementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LayoutManagerControl control)
                control.ApplyManagementVisibility();
        }

        private void ApplyManagementVisibility()
        {
            if (ManagementRoot == null)
                return;

            ManagementRoot.Visibility = ShowManagement ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LayoutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshing || _catalog == null)
                return;
            if (LayoutCombo.SelectedValue is not string id || id == _catalog.ActiveId)
                return;

            _catalog.SetActive(id);
            Report(UiLanguage.Format("StatusLayoutActivated", _catalog.Label(_catalog.Active)), false);
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_catalog == null)
                return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = UiLanguage.Get("LayoutFileFilter"),
                Multiselect = true,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
                return;

            var results = _catalog.ImportPaths(dialog.FileNames);
            int imported = results.Count(r => r.Status == LayoutImportStatus.Imported);
            int duplicates = results.Count(r => r.Status == LayoutImportStatus.Duplicate);
            int invalid = results.Count(r => r.Status == LayoutImportStatus.Invalid);

            if (imported > 0 && invalid > 0)
                Report(UiLanguage.Format("StatusLayoutImportPartial", imported, invalid), false);
            else if (imported == 1)
                Report(UiLanguage.Format("StatusLayoutImported", results.First(r => r.Status == LayoutImportStatus.Imported).Name), false);
            else if (imported > 1)
                Report(UiLanguage.Format("StatusLayoutImportedMany", imported, _catalog.Label(_catalog.Active)), false);
            else if (duplicates > 0 && invalid == 0)
                Report(UiLanguage.Format("StatusLayoutDuplicate", results.First(r => r.Status == LayoutImportStatus.Duplicate).Name), false);
            else
                Report(UiLanguage.Get("StatusLayoutImportFailed"), true);
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_catalog == null)
                return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = UiLanguage.Get("LayoutZipFilter"),
                FileName = "TypeFix-layouts.zip",
                AddExtension = true,
                DefaultExt = ".zip",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
                return;

            if (_catalog.ExportAll(dialog.FileName))
                Report(UiLanguage.Format("StatusLayoutExported", _catalog.Profiles.Count), false);
            else
                Report(UiLanguage.Get("StatusLayoutExportFailed"), true);
        }

        private void Report(string message, bool isError)
        {
            ReportStatus?.Invoke(message, isError);
        }

        private sealed class LayoutChoice
        {
            public LayoutChoice(string label, string id)
            {
                Label = label;
                Id = id;
            }

            public string Label { get; }
            public string Id { get; }
        }
    }
}
