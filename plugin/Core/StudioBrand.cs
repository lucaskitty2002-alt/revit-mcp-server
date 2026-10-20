using System.Windows;
using System.Windows.Media;

namespace revit_mcp_plugin.Core
{
    public static class StudioBrand
    {
        public const string Name = "VUE Studio";
        public const string Repository = "https://github.com/lucaskitty2002-alt/revit-mcp-server";
        public static readonly ImageSource OnIcon = PowerIcon("#0F766E");
        public static readonly ImageSource OffIcon = PowerIcon("#64748B");
        public static readonly ImageSource ErrorIcon = PowerIcon("#B45309");
        public static readonly ImageSource Logo = StudioIcon();

        private static ImageSource PowerIcon(string color)
        {
            var drawing = new DrawingGroup();
            using (DrawingContext dc = drawing.Open())
            {
                dc.DrawRoundedRectangle((Brush)new BrushConverter().ConvertFromString(color), null,
                    new Rect(1, 1, 30, 30), 8, 8);
                var pen = new Pen(Brushes.White, 2.4);
                dc.DrawGeometry(null, pen, Geometry.Parse("M 11,9 A 9,9 0 1 0 21,9"));
                dc.DrawLine(pen, new Point(16, 6), new Point(16, 16));
            }
            drawing.Freeze();
            var image = new DrawingImage(drawing); image.Freeze(); return image;
        }

        private static ImageSource StudioIcon()
        {
            var drawing = new DrawingGroup();
            using (DrawingContext dc = drawing.Open())
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(15, 23, 42)), null,
                    new Rect(1, 1, 30, 30), 8, 8);
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(94, 234, 212)), 2.5),
                    Geometry.Parse("M 7,9 L 16,24 L 25,9 M 16,6 L 16,15"));
            }
            drawing.Freeze();
            var image = new DrawingImage(drawing); image.Freeze(); return image;
        }
    }
}
