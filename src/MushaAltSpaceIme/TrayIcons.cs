using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MushaAltSpaceIme;

internal static class TrayIcons
{
    public static Icon CreateEnabled() => Create(Color.FromArgb(25, 103, 210), Color.White);

    public static Icon CreatePaused() => Create(Color.FromArgb(125, 125, 125), Color.WhiteSmoke);

    private static Icon Create(Color background, Color foreground)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(background))
        using (var textBrush = new SolidBrush(foreground))
        using (var font = new Font(FontFamily.GenericSansSerif, 21, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            graphics.FillEllipse(brush, 1, 1, 30, 30);
            graphics.DrawString("A", font, textBrush, new RectangleF(0, 3, 32, 28),
                format);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowedIcon = Icon.FromHandle(handle);
            return (Icon)borrowedIcon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
