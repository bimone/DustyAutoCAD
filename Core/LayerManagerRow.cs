using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace DustyAutoCAD.Core
{
    public class LayerManagerRow : INotifyPropertyChanged
    {
        // ── From scanner (read-only) ──────────────────────────────────────────
        public string SourceLayer { get; }
        public bool   IsUsed      { get; }
        public int    ObjectCount { get; }

        // ── From layer table ─────────────────────────────────────────────────
        public Color LayerColor { get; set; } = Colors.Gray;

        // ── Visibility ───────────────────────────────────────────────────────
        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(); }
        }

        // ── Mapping ──────────────────────────────────────────────────────────
        private bool _isIncluded;
        public bool IsIncluded
        {
            get => _isIncluded;
            set { _isIncluded = value; OnPropertyChanged(); }
        }

        private string _targetLayer = string.Empty;
        public string TargetLayer
        {
            get => _targetLayer;
            set { _targetLayer = value; OnPropertyChanged(); OnPropertyChanged(nameof(EffectiveTarget)); }
        }

        private string _linestyle = "Solid";
        public string Linestyle
        {
            get => _linestyle;
            set { _linestyle = value; OnPropertyChanged(); }
        }

        private string _intent = "printable";
        public string Intent
        {
            get => _intent;
            set { _intent = value; OnPropertyChanged(); OnPropertyChanged(nameof(EffectiveTarget)); }
        }

        // ── Computed ─────────────────────────────────────────────────────────
        public string EffectiveTarget => _intent?.ToLowerInvariant() switch
        {
            "reference" => _targetLayer + "_REF",
            "obstacle"  => _targetLayer + "_OBS",
            _           => _targetLayer
        };

        public LayerManagerRow(LayerInfo info, Color layerColor, bool isVisible)
        {
            SourceLayer = info.Name;
            IsUsed      = info.IsUsed;
            ObjectCount = info.ObjectCount;
            LayerColor  = layerColor;
            _isVisible  = isVisible;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
