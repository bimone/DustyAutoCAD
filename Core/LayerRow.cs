using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace DustyAutoCAD.Core
{
    public class LayerRow : INotifyPropertyChanged
    {
        public string LayerName  { get; set; } = "";
        public Color  LayerColor { get; set; } = Colors.Gray;

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
