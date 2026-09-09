using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using DustyAutoCAD.Core;
using DustyAutoCAD.UI;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(DustyAutoCAD.DustyPlugin))]
[assembly: CommandClass(typeof(DustyAutoCAD.DustyPlugin))]

namespace DustyAutoCAD
{
    public class DustyPlugin : IExtensionApplication
    {
        public void Initialize()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage("\nBIM One - Dusty AutoCAD Tools loaded.\n");

            ComponentManager.ItemInitialized += OnRibbonItemInitialized;
            TryBuildRibbon();
        }

        public void Terminate() { }

        [CommandMethod("DUSTYMAP")]
        public void DustyMap()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Application.ShowModalWindow(new DustyMapWindow(doc.Database));
        }

        [CommandMethod("DUSTYCHECK")]
        public void DustyCheck()
        {
            Application.ShowModelessWindow(new OverlapWindow());
        }

        [CommandMethod("DUSTYCP")]
        public void DustyCp()
        {
            Application.ShowModelessWindow(new ControlPointWindow());
        }

        [CommandMethod("DUSTYLAYERS")]
        public void DustyLayers()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Application.ShowModelessWindow(new LayerVisibilityWindow(doc.Database));
        }

        [CommandMethod("DUSTYLAYERMGR")]
        public void DustyLayerMgr()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Application.ShowModelessWindow(new LayerManagerWindow(doc.Database));
        }

        [CommandMethod("DUSTYPLACE")]
        public void DustyPlace()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            // Default name based on existing count
            string defaultName = ControlPointManager.NextName(doc.Database);

            var nameOpt = new PromptStringOptions($"\nControl point name [{defaultName}]: ")
            {
                AllowSpaces = false,
                UseDefaultValue = true,
                DefaultValue = defaultName,
            };
            var nameRes = ed.GetString(nameOpt);
            if (nameRes.Status != PromptStatus.OK) return;
            string name = string.IsNullOrWhiteSpace(nameRes.StringResult) ? defaultName : nameRes.StringResult;

            var descOpt = new PromptStringOptions("\nDescription (optional): ")
            {
                AllowSpaces = true,
                UseDefaultValue = true,
                DefaultValue = string.Empty,
            };
            var descRes = ed.GetString(descOpt);
            if (descRes.Status != PromptStatus.OK && descRes.Status != PromptStatus.None) return;
            string desc = descRes.StringResult ?? string.Empty;

            var ptOpt = new PromptPointOptions("\nPick location for control point: ");
            var ptRes = ed.GetPoint(ptOpt);
            if (ptRes.Status != PromptStatus.OK) return;

            ControlPointManager.PlaceControlPoint(doc.Database, ptRes.Value, name, desc);
            ed.WriteMessage($"\nPlaced control point: {name}\n");
        }

        // ----------------------------------------------------------------
        // Ribbon
        // ----------------------------------------------------------------

        private const string TabId   = "BIMOne_Tab";
        private const string PanelId = "BIMOne_Dusty_Panel";

        private void OnRibbonItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            ComponentManager.ItemInitialized -= OnRibbonItemInitialized;
            TryBuildRibbon();
        }

        private void TryBuildRibbon()
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;
            if (ribbon.FindTab(TabId) != null) return;

            var tab = new RibbonTab { Title = "BIM One", Id = TabId };

            var panelSrc = new RibbonPanelSource { Title = "Dusty Robotics", Id = PanelId };

            var btn = new RibbonButton
            {
                Text             = "Layer\nManager",
                CommandHandler   = new ShowLayerMgrHandler(),
                ShowText         = true,
                Size             = RibbonItemSize.Large,
                Orientation      = System.Windows.Controls.Orientation.Vertical,
                LargeImage       = LayerIcon.Make(32),
                Image            = LayerIcon.Make(16),
                Description      = "Open the unified Dusty Robotics Layer Manager",
                ToolTip          = new RibbonToolTip
                {
                    Title   = "Layer Manager",
                    Content = "Scan DWG layers, toggle visibility, and map to Dusty Robotics DR- naming. " +
                              "Save and load presets for reuse across projects.",
                    Command = "DUSTYLAYERMGR",
                },
            };

            panelSrc.Items.Add(btn);
            panelSrc.Items.Add(new RibbonSeparator());

            var btnCheck = new RibbonButton
            {
                Text             = "Check\nOverlaps",
                CommandHandler   = new ShowOverlapHandler(),
                ShowText         = true,
                Size             = RibbonItemSize.Large,
                Orientation      = System.Windows.Controls.Orientation.Vertical,
                LargeImage       = OverlapIcon.Make(32),
                Image            = OverlapIcon.Make(16),
                Description      = "Find overlapping lines in the current drawing",
                ToolTip          = new RibbonToolTip
                {
                    Title   = "Check Overlaps",
                    Content = "Scans model space for collinear overlapping line segments. " +
                              "Overlapping lines can corrupt Dusty linestyle rendering on site.",
                    Command = "DUSTYCHECK",
                },
            };

            panelSrc.Items.Add(btnCheck);
            panelSrc.Items.Add(new RibbonSeparator());

            var btnCp = new RibbonButton
            {
                Text             = "Control\nPoints",
                CommandHandler   = new ShowCpHandler(),
                ShowText         = true,
                Size             = RibbonItemSize.Large,
                Orientation      = System.Windows.Controls.Orientation.Vertical,
                LargeImage       = CpIcon.Make(32),
                Image            = CpIcon.Make(16),
                Description      = "Place and manage Dusty control points",
                ToolTip          = new RibbonToolTip
                {
                    Title   = "Control Points",
                    Content = "Place DUSTY_CP marker blocks and export them as a Dusty-standard CSV file. " +
                              "Use DUSTYPLACE to pick points interactively.",
                    Command = "DUSTYCP",
                },
            };

            panelSrc.Items.Add(btnCp);
            tab.Panels.Add(new RibbonPanel { Source = panelSrc });
            ribbon.Tabs.Add(tab);
        }
    }

    // ----------------------------------------------------------------
    // SendCommandHandler — used by modeless commands (DUSTYCHECK)
    // ----------------------------------------------------------------
    internal class SendCommandHandler : ICommand
    {
        public bool CanExecute(object parameter) => true;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public void Execute(object parameter)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute(parameter?.ToString() ?? string.Empty, true, false, true);
        }
    }

    internal class ShowOverlapHandler : ICommand
    {
        public bool CanExecute(object parameter) => true;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public void Execute(object parameter) =>
            Application.ShowModelessWindow(new OverlapWindow());
    }

    internal class ShowCpHandler : ICommand
    {
        public bool CanExecute(object parameter) => true;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public void Execute(object parameter) =>
            Application.ShowModelessWindow(new ControlPointWindow());
    }

    // ----------------------------------------------------------------
    // ShowLayerMgrHandler — opens unified Layer Manager (modeless)
    // ----------------------------------------------------------------
    internal class ShowLayerMgrHandler : ICommand
    {
        public bool CanExecute(object parameter) => true;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public void Execute(object parameter)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Application.ShowModelessWindow(new LayerManagerWindow(doc.Database));
        }
    }

    // ----------------------------------------------------------------
    // Programmatic robot icon in BIM One blue
    // ----------------------------------------------------------------
    internal static class RobotIcon
    {
        public static ImageSource Make(int size)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double s = size;

                // Background - dark blue rounded
                var bgBrush = new SolidColorBrush(Color.FromRgb(0x08, 0x03, 0x26));
                dc.DrawRoundedRectangle(bgBrush, null, new System.Windows.Rect(0, 0, s, s), s * 0.15, s * 0.15);

                var blue   = new SolidColorBrush(Color.FromRgb(0x5B, 0xA3, 0xF5));
                var white  = new SolidColorBrush(Colors.White);
                var silver = new SolidColorBrush(Color.FromRgb(0xCC, 0xD6, 0xE8));

                // Antenna stem
                var antennaPen = new Pen(silver, s * 0.07);
                dc.DrawLine(antennaPen,
                    new System.Windows.Point(s * 0.5, s * 0.04),
                    new System.Windows.Point(s * 0.5, s * 0.18));

                // Antenna ball
                dc.DrawEllipse(blue, null,
                    new System.Windows.Point(s * 0.5, s * 0.04), s * 0.07, s * 0.07);

                // Head
                dc.DrawRoundedRectangle(silver, null,
                    new System.Windows.Rect(s * 0.2, s * 0.18, s * 0.6, s * 0.38),
                    s * 0.08, s * 0.08);

                // Eyes - blue glowing dots
                dc.DrawEllipse(blue, null, new System.Windows.Point(s * 0.36, s * 0.35), s * 0.08, s * 0.08);
                dc.DrawEllipse(blue, null, new System.Windows.Point(s * 0.64, s * 0.35), s * 0.08, s * 0.08);

                // Mouth - small rectangle
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x1A, 0x2A, 0x4A)), null,
                    new System.Windows.Rect(s * 0.33, s * 0.46, s * 0.34, s * 0.06),
                    s * 0.03, s * 0.03);

                // Body
                dc.DrawRoundedRectangle(silver, null,
                    new System.Windows.Rect(s * 0.25, s * 0.59, s * 0.5, s * 0.32),
                    s * 0.06, s * 0.06);

                // Body blue chest stripe
                dc.DrawRoundedRectangle(blue, null,
                    new System.Windows.Rect(s * 0.35, s * 0.65, s * 0.3, s * 0.1),
                    s * 0.03, s * 0.03);
            }

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
    }

    // ----------------------------------------------------------------
    // Control point icon — crosshair circle with CP label
    // ----------------------------------------------------------------
    internal static class CpIcon
    {
        public static ImageSource Make(int size)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double s = size;
                var bg   = new SolidColorBrush(Color.FromRgb(0x08, 0x03, 0x26));
                var blue = new SolidColorBrush(Color.FromRgb(0x5B, 0xA3, 0xF5));
                var cyan = new SolidColorBrush(Color.FromRgb(0x00, 0xCC, 0xCC));
                var pen  = new Pen(cyan, s * 0.07);

                dc.DrawRoundedRectangle(bg, null,
                    new System.Windows.Rect(0, 0, s, s), s * 0.15, s * 0.15);

                double cx = s * 0.42, cy = s * 0.42, r = s * 0.26;

                // Circle
                dc.DrawEllipse(null, pen, new System.Windows.Point(cx, cy), r, r);

                // Crosshair lines
                dc.DrawLine(pen, new System.Windows.Point(cx - r * 1.4, cy), new System.Windows.Point(cx + r * 1.4, cy));
                dc.DrawLine(pen, new System.Windows.Point(cx, cy - r * 1.4), new System.Windows.Point(cx, cy + r * 1.4));

                // "CP" label (tiny text via glyphs — use a filled rect as shorthand at small sizes)
                if (size >= 24)
                {
                    var ft = new FormattedText("CP",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Windows.FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"),
                        s * 0.28, blue, 1.0);
                    dc.DrawText(ft, new System.Windows.Point(s * 0.58, s * 0.60));
                }
            }

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
    }

    // ----------------------------------------------------------------
    // Layer visibility icon — stack of horizontal lines in BIM One blue
    // ----------------------------------------------------------------
    internal static class LayerIcon
    {
        public static ImageSource Make(int size)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double s    = size;
                var bg      = new SolidColorBrush(Color.FromRgb(0x08, 0x03, 0x26));
                var blue    = new SolidColorBrush(Color.FromRgb(0x5B, 0xA3, 0xF5));
                var cyan    = new SolidColorBrush(Color.FromRgb(0x00, 0xCC, 0xCC));
                var dim     = new SolidColorBrush(Color.FromRgb(0x44, 0x55, 0x77));

                dc.DrawRoundedRectangle(bg, null, new System.Windows.Rect(0, 0, s, s), s * 0.15, s * 0.15);

                // 4 horizontal lines of varying width, representing layers
                double lh = s * 0.08;  // line height
                double lm = s * 0.12;  // left margin
                double[] widths = { 0.76, 0.55, 0.68, 0.42 };
                double[] tops   = { 0.18, 0.34, 0.50, 0.66 };
                var brushes = new[] { cyan, blue, blue, dim };

                for (int i = 0; i < 4; i++)
                {
                    dc.DrawRoundedRectangle(brushes[i], null,
                        new System.Windows.Rect(lm, s * tops[i], s * widths[i], lh),
                        lh * 0.4, lh * 0.4);
                }

                // Eye indicator on row 0 (cyan row)
                double ex = s * 0.82, ey = s * 0.18 + lh * 0.5;
                double er = lh * 0.5;
                dc.DrawEllipse(null, new Pen(cyan, s * 0.04),
                    new System.Windows.Point(ex, ey), er, er * 0.65);
                dc.DrawEllipse(cyan, null,
                    new System.Windows.Point(ex, ey), er * 0.35, er * 0.35 * 0.65);
            }

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
    }

    // ----------------------------------------------------------------
    // Overlap check icon — magnifier + warning triangle in BIM One blue
    // ----------------------------------------------------------------
    internal static class OverlapIcon
    {
        public static ImageSource Make(int size)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double s = size;

                var bg   = new SolidColorBrush(Color.FromRgb(0x08, 0x03, 0x26));
                var blue = new SolidColorBrush(Color.FromRgb(0x5B, 0xA3, 0xF5));
                var warn = new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23));
                var white = new SolidColorBrush(Colors.White);

                dc.DrawRoundedRectangle(bg, null,
                    new System.Windows.Rect(0, 0, s, s), s * 0.15, s * 0.15);

                // Two overlapping horizontal lines (the problem being detected)
                var linePen1 = new Pen(blue, s * 0.07);
                var linePen2 = new Pen(warn, s * 0.07);
                dc.DrawLine(linePen1,
                    new System.Windows.Point(s * 0.1, s * 0.35),
                    new System.Windows.Point(s * 0.9, s * 0.35));
                dc.DrawLine(linePen2,
                    new System.Windows.Point(s * 0.3, s * 0.35),
                    new System.Windows.Point(s * 0.7, s * 0.35));

                // Warning triangle at bottom
                var triPts = new System.Windows.Media.PathGeometry();
                var fig    = new System.Windows.Media.PathFigure
                {
                    StartPoint = new System.Windows.Point(s * 0.5,  s * 0.55),
                    IsClosed   = true,
                };
                fig.Segments.Add(new System.Windows.Media.LineSegment(new System.Windows.Point(s * 0.2, s * 0.92), true));
                fig.Segments.Add(new System.Windows.Media.LineSegment(new System.Windows.Point(s * 0.8, s * 0.92), true));
                triPts.Figures.Add(fig);
                dc.DrawGeometry(warn, null, triPts);

                // Exclamation mark inside triangle
                dc.DrawRectangle(bg, null,
                    new System.Windows.Rect(s * 0.46, s * 0.67, s * 0.08, s * 0.14));
                dc.DrawEllipse(bg, null,
                    new System.Windows.Point(s * 0.5, s * 0.86), s * 0.04, s * 0.04);
            }

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
    }
}
