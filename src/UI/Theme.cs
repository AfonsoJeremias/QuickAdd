using System.Windows.Media;

namespace QuickAdd.UI
{
    public enum ModeKind { Add, Move, Scan, Rename }

    public static class Theme
    {
        static Brush Rgb(byte r, byte g, byte b) { var br = new SolidColorBrush(Color.FromRgb(r, g, b)); br.Freeze(); return br; }

        public static readonly Brush Add = Rgb(0x2F, 0x7D, 0xD1);
        public static readonly Brush Move = Rgb(0xE0, 0x8A, 0x1E);
        public static readonly Brush Scan = Rgb(0x2F, 0xA3, 0x6B);
        public static readonly Brush Rename = Rgb(0x9B, 0x5D, 0xD1);
        public static readonly Brush KeyCap = Rgb(0x2A, 0x31, 0x38);
        public static readonly Brush Muted = Rgb(0xA0, 0xA8, 0xB0);
        public static readonly Brush FaceA = Rgb(0x3B, 0xA5, 0x5D);
        public static readonly Brush FaceB = Rgb(0xE0, 0x48, 0x48);
        public static readonly Brush FaceX = Rgb(0x2F, 0x7D, 0xD1);
        public static readonly Brush FaceY = Rgb(0xD9, 0xA8, 0x1E);

        public static Brush Of(ModeKind m)
        {
            switch (m)
            {
                case ModeKind.Move: return Move;
                case ModeKind.Scan: return Scan;
                case ModeKind.Rename: return Rename;
                default: return Add;
            }
        }

        public static Brush Face(string key)
        {
            switch (key)
            {
                case "A": return FaceA;
                case "B": return FaceB;
                case "X": return FaceX;
                case "Y": return FaceY;
                default: return null;
            }
        }
    }
}
