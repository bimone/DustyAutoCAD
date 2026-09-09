using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.AutoCAD.DatabaseServices;
using DustyAutoCAD.Core;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DustyAutoCAD.UI
{
    public partial class LayerVisibilityWindow : Window
    {
        private readonly Database                     _db;
        private readonly ObservableCollection<LayerRow> _rows = new();
        private          ICollectionView              _view  = null!;

        public LayerVisibilityWindow(Database db)
        {
            _db = db;
            InitializeComponent();
            Refresh();
            PopulatePresetCombo();
        }

        // -----------------------------------------------------------------------
        // Data loading
        // -----------------------------------------------------------------------

        private void Refresh()
        {
            _rows.Clear();

            using var tr = _db.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(_db.LayerTableId, OpenMode.ForRead);

            foreach (ObjectId layerId in lt)
            {
                var ltr = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForRead);
                _rows.Add(new LayerRow
                {
                    LayerName  = ltr.Name,
                    LayerColor = AciToWpf(ltr.Color.ColorIndex),
                    IsVisible  = !ltr.IsOff,
                });
            }
            tr.Commit();

            // Sort by name
            var sorted = _rows.OrderBy(r => r.LayerName).ToList();
            _rows.Clear();
            foreach (var r in sorted) _rows.Add(r);

            _view = CollectionViewSource.GetDefaultView(_rows);
            grid_layers.ItemsSource = _view;

            UpdateCount();
        }

        private void UpdateCount() =>
            txt_count.Text = $"{_rows.Count} layer(s)";

        private void PopulatePresetCombo()
        {
            preset_combo.Items.Clear();
            foreach (var name in LayerPresetStore.GetPresetNames())
                preset_combo.Items.Add(name);
        }

        // -----------------------------------------------------------------------
        // Color row swatch (set via row Loaded event since XAML binding for
        // System.Windows.Media.Color → SolidColorBrush needs code-behind)
        // -----------------------------------------------------------------------

        private void DataGridRow_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not DataGridRow row) return;
            if (row.DataContext is not LayerRow lr) return;

            // Walk the visual tree to find the Border named colorSwatch
            var cell = GetCell(row, 1);
            if (cell == null) return;

            var border = FindVisualChild<Border>(cell);
            if (border != null)
                border.Background = new SolidColorBrush(lr.LayerColor);
        }

        private static DataGridCell? GetCell(DataGridRow row, int columnIndex)
        {
            var presenter = FindVisualChild<DataGridCellsPresenter>(row);
            if (presenter == null) return null;
            var cell = presenter.ItemContainerGenerator.ContainerFromIndex(columnIndex) as DataGridCell;
            return cell;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        // -----------------------------------------------------------------------
        // Search filter
        // -----------------------------------------------------------------------

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = txt_search.Text;
            _view.Filter = string.IsNullOrWhiteSpace(text)
                ? null
                : obj => obj is LayerRow lr && lr.LayerName.Contains(text, StringComparison.OrdinalIgnoreCase);
            _view.Refresh();
        }

        // -----------------------------------------------------------------------
        // Bulk visibility
        // -----------------------------------------------------------------------

        private void BtnAllOn_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.IsVisible = true;
            txt_status.Text = "All layers set visible.";
        }

        private void BtnAllOff_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.IsVisible = false;
            txt_status.Text = "All layers hidden.";
        }

        private void BtnInvert_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.IsVisible = !r.IsVisible;
            txt_status.Text = "Visibility inverted.";
        }

        // -----------------------------------------------------------------------
        // Presets
        // -----------------------------------------------------------------------

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var name = txt_preset_name.Text.Trim();
            if (string.IsNullOrEmpty(name)) { txt_status.Text = "Enter a preset name first."; return; }

            var preset = new LayerPreset { Name = name };
            foreach (var r in _rows)
                preset.Layers[r.LayerName] = r.IsVisible;

            LayerPresetStore.Save(preset);
            PopulatePresetCombo();
            txt_status.Text = $"Preset \"{name}\" saved.";
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            var name = preset_combo.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) { txt_status.Text = "Select a preset to load."; return; }

            var preset = LayerPresetStore.Load(name);
            if (preset == null) { txt_status.Text = "Could not load preset."; return; }

            foreach (var r in _rows)
            {
                if (preset.Layers.TryGetValue(r.LayerName, out var vis))
                    r.IsVisible = vis;
            }
            txt_status.Text = "Preset loaded.";
        }

        // -----------------------------------------------------------------------
        // Apply to drawing
        // -----------------------------------------------------------------------

        // -----------------------------------------------------------------------
        // Inspection panel + Select in Drawing
        // -----------------------------------------------------------------------

        private string? _selectedLayerName;

        private void Grid_Layers_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (grid_layers.SelectedItem is not LayerRow row)
            {
                txt_info.Text = "Select a layer row to inspect its entities.";
                btn_select_in_drawing.IsEnabled = false;
                _selectedLayerName = null;
                return;
            }

            _selectedLayerName = row.LayerName;
            btn_select_in_drawing.IsEnabled = true;

            try
            {
                using var tr = _db.TransactionManager.StartTransaction();
                var bt  = (BlockTable)tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                var groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int total  = 0;

                foreach (ObjectId id in btr)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                    if (!string.Equals(ent.Layer, row.LayerName, StringComparison.OrdinalIgnoreCase)) continue;
                    total++;
                    var typeName = ent.GetType().Name;
                    groups[typeName] = groups.TryGetValue(typeName, out var n) ? n + 1 : 1;
                }
                tr.Commit();

                if (total == 0)
                {
                    txt_info.Text = $"{row.LayerName}  —  No entities in model space on this layer.";
                    return;
                }

                var breakdown = string.Join("  ·  ",
                    groups.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value}"));
                txt_info.Text = $"{row.LayerName}  —  {total} entit{(total == 1 ? "y" : "ies")}  ·  {breakdown}";
            }
            catch (Exception ex)
            {
                txt_info.Text = $"{row.LayerName}  —  (Query failed: {ex.Message})";
            }
        }

        private void BtnSelectInDrawing_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedLayerName == null) return;

            var acDoc = Application.DocumentManager.MdiActiveDocument;
            if (acDoc == null) return;

            try
            {
                var ids = new List<ObjectId>();

                using var tr = _db.TransactionManager.StartTransaction();
                var bt  = (BlockTable)tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in btr)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity ent &&
                        string.Equals(ent.Layer, _selectedLayerName, StringComparison.OrdinalIgnoreCase))
                    {
                        ids.Add(id);
                    }
                }
                tr.Commit();

                if (ids.Count == 0)
                {
                    txt_status.Text = "No entities found on that layer.";
                    return;
                }

                using var docLock = acDoc.LockDocument();
                acDoc.Editor.SetImpliedSelection(ids.ToArray());
                txt_status.Text = $"Selected {ids.Count} entit{(ids.Count == 1 ? "y" : "ies")} on layer \"{_selectedLayerName}\".";
            }
            catch (Exception ex)
            {
                txt_status.Text = $"Select failed: {ex.Message}";
            }
        }

        // -----------------------------------------------------------------------
        // Apply to drawing
        // -----------------------------------------------------------------------

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var acDoc = Application.DocumentManager.MdiActiveDocument;
            if (acDoc == null) return;

            try
            {
                using var docLock = acDoc.LockDocument();
                using var tr = _db.TransactionManager.StartTransaction();
                var lt = (LayerTable)tr.GetObject(_db.LayerTableId, OpenMode.ForRead);

                foreach (var row in _rows)
                {
                    if (!lt.Has(row.LayerName)) continue;
                    var ltr = (LayerTableRecord)tr.GetObject(lt[row.LayerName], OpenMode.ForWrite);
                    ltr.IsOff = !row.IsVisible;
                }

                tr.Commit();
                txt_status.Text = "Layer visibility applied.";
                acDoc.Editor.Regen();
            }
            catch (Exception ex)
            {
                txt_status.Text = $"Error: {ex.Message}";
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            Refresh();
            txt_status.Text = "Refreshed from drawing.";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        // -----------------------------------------------------------------------
        // ACI color → WPF Color
        // -----------------------------------------------------------------------

        private static Color AciToWpf(int aci) => aci switch
        {
            1 => Colors.Red,
            2 => Colors.Yellow,
            3 => Colors.Green,
            4 => Colors.Cyan,
            5 => Colors.Blue,
            6 => Color.FromRgb(255, 0, 255),
            7 => Colors.White,
            _ => Color.FromRgb(128, 128, 128),
        };
    }
}
