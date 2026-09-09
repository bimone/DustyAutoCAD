using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using Autodesk.AutoCAD.DatabaseServices;
using DustyAutoCAD.Core;
using Microsoft.Win32;

namespace DustyAutoCAD.UI
{
    public partial class DustyMapWindow : Window
    {
        private readonly Database _db;
        private ObservableCollection<LayerRowViewModel> _rows = new();
        private List<LayerInfo> _scannedLayers = new();

        private static string LastUsedPath =>
            Path.Combine(AcadPreset.DefaultPresetDir, "_last_used.json");

        public DustyMapWindow(Database db)
        {
            _db = db;
            InitializeComponent();
            grid_layers.ItemsSource = _rows;

            // Restore last session's mappings if available
            AcadPreset? last = null;
            if (File.Exists(LastUsedPath))
            {
                try { last = AcadPreset.Load(LastUsedPath); } catch { }
            }
            ScanAndPopulate(last);
            if (last != null)
                txt_preset_name.Text = last.PresetName;

            Closing += OnWindowClosing;
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(AcadPreset.DefaultPresetDir);
                var snapshot = new AcadPreset
                {
                    PresetName = txt_preset_name.Text.Trim() is { Length: > 0 } n ? n : "Last Used",
                    Mappings   = _rows
                        .Where(r => !string.IsNullOrWhiteSpace(r.TargetLayer))
                        .Select(r => new AcadLayerMapping
                        {
                            Source    = r.SourceLayer,
                            Target    = r.TargetLayer,
                            Linestyle = r.Linestyle,
                            Intent    = r.Intent
                        })
                        .ToList()
                };
                snapshot.Save(LastUsedPath);
            }
            catch { /* non-fatal */ }
        }

        private void ScanAndPopulate(AcadPreset? preset)
        {
            try
            {
                _scannedLayers = LayerScanner.ScanLayers(_db);
                var newRows = LayerMapper.BuildRows(_scannedLayers, preset);
                _rows.Clear();
                foreach (var r in newRows)
                    _rows.Add(r);

                txt_status.Text = $"{_scannedLayers.Count} layer(s) found in drawing.";
            }
            catch (Exception ex)
            {
                txt_status.Text = $"Scan failed: {ex.Message}";
            }
        }

        private void header_drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void btn_close_click(object sender, RoutedEventArgs e) => Close();

        private void btn_scan_click(object sender, RoutedEventArgs e)
        {
            // Preserve any mappings the user has typed in
            var existing = _rows.ToDictionary(
                r => r.SourceLayer,
                r => (r.TargetLayer, r.Linestyle, r.Intent, r.IsIncluded),
                StringComparer.OrdinalIgnoreCase);

            _scannedLayers = LayerScanner.ScanLayers(_db);
            var newRows = LayerMapper.BuildRows(_scannedLayers, null);

            _rows.Clear();
            foreach (var row in newRows)
            {
                if (existing.TryGetValue(row.SourceLayer, out var saved))
                {
                    row.TargetLayer = saved.TargetLayer;
                    row.Linestyle   = saved.Linestyle;
                    row.Intent      = saved.Intent;
                    row.IsIncluded  = saved.IsIncluded;
                }
                _rows.Add(row);
            }

            txt_status.Text = $"{_scannedLayers.Count} layer(s) found.";
        }

        private void btn_load_preset_click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title            = "Load Preset",
                Filter           = "Preset files (*.json)|*.json|All files (*.*)|*.*",
                InitialDirectory = Directory.Exists(AcadPreset.DefaultPresetDir)
                                       ? AcadPreset.DefaultPresetDir
                                       : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var preset = AcadPreset.Load(dlg.FileName);
                txt_preset_name.Text = preset.PresetName;

                // Rebuild rows with preset auto-fill (re-scan to stay current)
                _scannedLayers = LayerScanner.ScanLayers(_db);
                var newRows = LayerMapper.BuildRows(_scannedLayers, preset);

                _rows.Clear();
                foreach (var r in newRows)
                    _rows.Add(r);

                var filled = _rows.Count(r => r.IsIncluded);
                txt_status.Text = $"Preset \"{preset.PresetName}\" loaded - {filled} mapping(s) auto-filled.";
            }
            catch (Exception ex)
            {
                txt_status.Text = $"Load failed: {ex.Message}";
                MessageBox.Show($"Could not load preset:\n{ex.Message}", "Load Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void btn_save_preset_click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(AcadPreset.DefaultPresetDir);

            var dlg = new SaveFileDialog
            {
                Title            = "Save Preset",
                Filter           = "Preset files (*.json)|*.json",
                InitialDirectory = AcadPreset.DefaultPresetDir,
                FileName         = string.IsNullOrWhiteSpace(txt_preset_name.Text)
                                       ? "MyPreset"
                                       : txt_preset_name.Text
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var preset = new AcadPreset
                {
                    PresetName = Path.GetFileNameWithoutExtension(dlg.FileName),
                    Mappings   = _rows
                        .Where(r => !string.IsNullOrWhiteSpace(r.TargetLayer))
                        .Select(r => new AcadLayerMapping
                        {
                            Source    = r.SourceLayer,
                            Target    = r.TargetLayer,
                            Linestyle = r.Linestyle,
                            Intent    = r.Intent
                        })
                        .ToList()
                };

                preset.Save(dlg.FileName);
                txt_preset_name.Text = preset.PresetName;
                txt_status.Text = $"Preset saved: {dlg.FileName}";
            }
            catch (Exception ex)
            {
                txt_status.Text = $"Save failed: {ex.Message}";
                MessageBox.Show($"Could not save preset:\n{ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void btn_apply_click(object sender, RoutedEventArgs e)
        {
            var included = _rows.Where(r => r.IsIncluded && !string.IsNullOrWhiteSpace(r.TargetLayer)).ToList();
            if (included.Count == 0)
            {
                txt_status.Text = "No rows selected - nothing to apply.";
                return;
            }

            try
            {
                txt_status.Text = "Applying...";

                var result = LayerMapper.ApplyMappings(_db, included);

                if (chk_purge.IsChecked == true)
                    LayerMapper.PurgeLayers(_db, _rows);

                if (chk_freeze_ref.IsChecked == true)
                    LayerMapper.FreezeLayers(_db, _rows, "reference");

                var summary = $"Done - {result.Renamed} renamed, {result.Skipped} skipped.";
                if (result.Errors.Count > 0)
                    summary += $" {result.Errors.Count} error(s).";

                txt_status.Text = summary;

                if (result.Errors.Count > 0)
                {
                    MessageBox.Show("Completed with errors:\n\n" + string.Join("\n", result.Errors.Take(10)),
                        "Apply Result", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                txt_status.Text = $"Apply failed: {ex.Message}";
                MessageBox.Show($"Apply failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
