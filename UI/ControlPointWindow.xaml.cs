using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using Autodesk.AutoCAD.DatabaseServices;
using DustyAutoCAD.Core;
using Microsoft.Win32;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DustyAutoCAD.UI
{
    public partial class ControlPointWindow : Window
    {
        private readonly ObservableCollection<ControlPoint> _rows = new();

        public ControlPointWindow()
        {
            InitializeComponent();
            grid_cps.ItemsSource = _rows;
            Refresh();
        }

        private void header_drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void btn_close_click(object sender, RoutedEventArgs e) => Close();

        // ----------------------------------------------------------------
        // Refresh — re-scans drawing for DUSTY_CP blocks
        // ----------------------------------------------------------------

        public void Refresh()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { txt_status.Text = "No active document."; return; }

            try
            {
                var pts = ControlPointManager.GetAll(doc.Database);
                _rows.Clear();
                foreach (var p in pts) _rows.Add(p);
                txt_summary.Text = $"{_rows.Count} control point(s) in drawing.";
                txt_status.Text  = "Ready.";
            }
            catch (Exception ex)
            {
                txt_status.Text = $"Refresh failed: {ex.Message}";
            }
        }

        private void btn_refresh_click(object sender, RoutedEventArgs e) => Refresh();

        // ----------------------------------------------------------------
        // Place CP — sends DUSTYPLACE command so user can pick interactively
        // ----------------------------------------------------------------

        private void btn_place_click(object sender, RoutedEventArgs e)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // DUSTYPLACE is interactive — runs in AutoCAD command context
            // Window stays open; user clicks Refresh after placing
            doc.SendStringToExecute("DUSTYPLACE\n", true, false, true);
            txt_status.Text = "Switched to drawing - pick a location for the control point. Click Refresh when done.";
        }

        // ----------------------------------------------------------------
        // Remove selected
        // ----------------------------------------------------------------

        private void grid_cps_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            btn_remove.IsEnabled = grid_cps.SelectedItem is ControlPoint;
        }

        private void btn_remove_click(object sender, RoutedEventArgs e)
        {
            if (grid_cps.SelectedItem is not ControlPoint cp) return;

            var confirm = MessageBox.Show(
                $"Remove control point \"{cp.Name}\" from the drawing?",
                "Remove Control Point",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                using var docLock = doc.LockDocument();
                using var tr = doc.Database.TransactionManager.StartTransaction();
                var ent = (Entity)tr.GetObject(cp.BlockRefId, OpenMode.ForWrite);
                ent.Erase();
                tr.Commit();

                _rows.Remove(cp);
                txt_summary.Text = $"{_rows.Count} control point(s) in drawing.";
                txt_status.Text  = $"Removed {cp.Name}.";
            }
            catch (Exception ex)
            {
                txt_status.Text = $"Remove failed: {ex.Message}";
            }
        }

        // ----------------------------------------------------------------
        // Export CSV
        // ----------------------------------------------------------------

        private void btn_export_click(object sender, RoutedEventArgs e)
        {
            if (_rows.Count == 0)
            {
                txt_status.Text = "No control points to export.";
                return;
            }

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            string defaultName = doc != null
                ? Path.GetFileNameWithoutExtension(doc.Name) + "_ControlPoints"
                : "ControlPoints";

            var dlg = new SaveFileDialog
            {
                Title      = "Export Control Points",
                Filter     = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName   = defaultName,
                DefaultExt = ".csv",
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var csv = ControlPointManager.ToCsv(_rows);
                // No BOM — Dusty Portal and most web parsers reject UTF-8 BOM
                File.WriteAllText(dlg.FileName, csv, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                txt_status.Text = $"Exported {_rows.Count} point(s) to {Path.GetFileName(dlg.FileName)}.";

                // Offer to open the folder
                if (MessageBox.Show("Export complete. Open containing folder?", "Export",
                        MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName        = Path.GetDirectoryName(dlg.FileName)!,
                        UseShellExecute = true,
                    });
                }
            }
            catch (Exception ex)
            {
                txt_status.Text = $"Export failed: {ex.Message}";
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
