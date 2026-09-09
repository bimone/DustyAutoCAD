using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using Autodesk.AutoCAD.ApplicationServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DustyAutoCAD.Core;

namespace DustyAutoCAD.UI
{
    public class OverlapRow
    {
        public int     RowNum          { get; set; }
        public string  Layer1          { get; set; } = string.Empty;
        public string  Layer2          { get; set; } = string.Empty;
        public string  OverlapDisplay  { get; set; } = string.Empty;
        public string  StrategyLabel   { get; set; } = string.Empty;
        public string  Location        { get; set; } = string.Empty;
        public OverlapPair Pair        { get; set; } = null!;
    }

    public partial class OverlapWindow : Window
    {
        private readonly ObservableCollection<OverlapRow> _rows = new();
        private List<OverlapPair> _pairs = new();

        public OverlapWindow()
        {
            InitializeComponent();
            grid_overlaps.ItemsSource = _rows;
            grid_overlaps.MouseDoubleClick += (_, _) => btn_zoom_click(this, new RoutedEventArgs());
        }

        private void header_drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void btn_close_click(object sender, RoutedEventArgs e) => Close();

        // ----------------------------------------------------------------
        // Scan
        // ----------------------------------------------------------------

        private void btn_scan_click(object sender, RoutedEventArgs e)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { txt_status.Text = "No active document."; return; }

            txt_status.Text  = "Scanning...";
            txt_summary.Text = "Scanning model space for overlapping lines...";
            _rows.Clear();
            btn_zoom.IsEnabled         = false;
            btn_fix_selected.IsEnabled = false;
            btn_fix_all.IsEnabled      = false;
            tog_line1.IsEnabled        = false;
            tog_line2.IsEnabled        = false;
            tog_line1.IsChecked        = false;
            tog_line2.IsChecked        = false;

            try
            {
                _pairs = OverlapChecker.FindOverlaps(doc.Database);

                for (int i = 0; i < _pairs.Count; i++)
                {
                    var p = _pairs[i];
                    _rows.Add(new OverlapRow
                    {
                        RowNum         = i + 1,
                        Layer1         = p.Layer1,
                        Layer2         = p.Layer2,
                        OverlapDisplay = $"{p.OverlapLength:F4}",
                        StrategyLabel  = p.StrategyLabel,
                        Location       = p.Location,
                        Pair           = p,
                    });
                }

                if (_pairs.Count == 0)
                {
                    txt_summary.Text = "No overlapping lines found.";
                    txt_status.Text  = "Clean - no overlaps detected.";
                }
                else
                {
                    int fixable    = _pairs.Count(p => p.CanAutoFix);
                    int selectOnly = _pairs.Count - fixable;
                    txt_summary.Text = $"{_pairs.Count} overlap(s): {fixable} auto-fixable, {selectOnly} require manual review.";
                    txt_status.Text  = "Select a row to zoom and highlight. Double-click to zoom.";
                    btn_fix_all.IsEnabled = fixable > 0;
                }
            }
            catch (Exception ex)
            {
                txt_status.Text  = $"Scan failed: {ex.Message}";
                txt_summary.Text = "Scan error.";
            }
        }

        // ----------------------------------------------------------------
        // Row selection — auto-set toggles to fix target, update drawing selection
        // ----------------------------------------------------------------

        private void grid_overlaps_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (grid_overlaps.SelectedItem is not OverlapRow row)
            {
                btn_zoom.IsEnabled         = false;
                btn_fix_selected.IsEnabled = false;
                tog_line1.IsEnabled        = false;
                tog_line2.IsEnabled        = false;
                return;
            }

            btn_zoom.IsEnabled         = true;
            btn_fix_selected.IsEnabled = true;
            tog_line1.IsEnabled        = true;
            tog_line2.IsEnabled        = true;

            // Auto-set toggles to whichever line the fix command would delete
            // SelectOnly pairs default to both toggled on
            tog_line1.IsChecked = row.Pair.Strategy == FixStrategy.DeleteLine1
                                  || row.Pair.Strategy == FixStrategy.SelectOnly;
            tog_line2.IsChecked = row.Pair.Strategy == FixStrategy.DeleteLine2
                                  || row.Pair.Strategy == FixStrategy.SelectOnly;

            UpdateDrawingSelection(row);
        }

        // Toggle clicked — update selection without changing the row
        private void tog_line_click(object sender, RoutedEventArgs e)
        {
            if (grid_overlaps.SelectedItem is OverlapRow row)
                UpdateDrawingSelection(row);
        }

        private void UpdateDrawingSelection(OverlapRow row)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ids = new List<ObjectId>();
            if (tog_line1.IsChecked == true) ids.Add(row.Pair.Line1Id);
            if (tog_line2.IsChecked == true) ids.Add(row.Pair.Line2Id);

            if (ids.Count == 0) return;

            try { doc.Editor.SetImpliedSelection(ids.ToArray()); }
            catch { }

            string which = (tog_line1.IsChecked == true, tog_line2.IsChecked == true) switch
            {
                (true,  true)  => "both lines",
                (true,  false) => "Line 1",
                (false, true)  => "Line 2",
                _              => "nothing",
            };
            txt_status.Text = $"Overlap #{row.RowNum} - selected {which}.";
        }

        // ----------------------------------------------------------------
        // Zoom To — zooms without disturbing selection
        // ----------------------------------------------------------------

        private void btn_zoom_click(object sender, RoutedEventArgs e)
        {
            if (grid_overlaps.SelectedItem is not OverlapRow row) return;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var center = row.Pair.ZoomCenter;
            double pad = Math.Max(row.Pair.OverlapLength * 5, 2.0);

            try
            {
                var view = doc.Editor.GetCurrentView();
                view.CenterPoint = new Autodesk.AutoCAD.Geometry.Point2d(center.X, center.Y);
                view.Height = pad * 2;
                view.Width  = pad * 2;
                doc.Editor.SetCurrentView(view);
            }
            catch { }

            // Re-apply selection after view change
            UpdateDrawingSelection(row);
        }

        // ----------------------------------------------------------------
        // Fix Selected — removes Line2 from the overlapping pair
        // ----------------------------------------------------------------

        private void btn_fix_selected_click(object sender, RoutedEventArgs e)
        {
            if (grid_overlaps.SelectedItem is not OverlapRow row) return;

            if (!row.Pair.CanAutoFix)
            {
                txt_status.Text = "This overlap is partial - neither line can be safely removed. Delete manually.";
                return;
            }

            FixPairs(new[] { row.Pair });
            _rows.Remove(row);
            txt_summary.Text = $"{_rows.Count} overlap(s) remaining.";
            txt_status.Text  = $"Fixed: deleted the contained line from pair #{row.RowNum}.";

            if (_rows.Count == 0)
            {
                btn_fix_all.IsEnabled      = false;
                btn_fix_selected.IsEnabled = false;
                btn_zoom.IsEnabled         = false;
                txt_status.Text = "All overlaps resolved.";
            }
        }

        // ----------------------------------------------------------------
        // Fix All — removes Line2 from every overlapping pair
        // ----------------------------------------------------------------

        private void btn_fix_all_click(object sender, RoutedEventArgs e)
        {
            var fixable = _pairs.Where(p => p.CanAutoFix).ToList();
            if (fixable.Count == 0) return;

            int selectOnly = _pairs.Count - fixable.Count;
            string msg = $"Will delete the contained line from {fixable.Count} auto-fixable pair(s).";
            if (selectOnly > 0)
                msg += $"\n\n{selectOnly} partial overlap(s) will be skipped - review those manually.";
            msg += "\n\nProceed?";

            if (MessageBox.Show(msg, "Fix All", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;

            FixPairs(fixable);

            // Remove fixed rows, keep select-only rows
            var toRemove = _rows.Where(r => r.Pair.CanAutoFix).ToList();
            foreach (var r in toRemove) _rows.Remove(r);
            _pairs = _pairs.Where(p => !p.CanAutoFix).ToList();

            if (_rows.Count == 0)
            {
                btn_fix_all.IsEnabled      = false;
                btn_fix_selected.IsEnabled = false;
                btn_zoom.IsEnabled         = false;
                txt_summary.Text = "All auto-fixable overlaps resolved.";
                txt_status.Text  = "Done. Re-scan to verify.";
            }
            else
            {
                btn_fix_all.IsEnabled = false;
                txt_summary.Text = $"{_rows.Count} partial overlap(s) remain - review manually.";
                txt_status.Text  = "Auto-fix complete. Remaining pairs require manual review.";
            }
        }

        private static void FixPairs(IEnumerable<OverlapPair> pairs)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using var docLock = doc.LockDocument();
            using var tr = doc.Database.TransactionManager.StartTransaction();

            foreach (var pair in pairs)
            {
                try
                {
                    var ent = (Entity)tr.GetObject(pair.IdToDelete, OpenMode.ForWrite);
                    ent.Erase();
                }
                catch { /* already erased or locked — skip */ }
            }

            tr.Commit();
        }

        // ----------------------------------------------------------------
        // Footer hyperlink
        // ----------------------------------------------------------------

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
