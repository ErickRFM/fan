using System.Drawing;
using System.Drawing.Drawing2D;

namespace FanForge;

/// <summary>Minimal auto-scaling line chart for a bounded series of nullable samples.</summary>
internal sealed class Sparkline : Control
{
    private readonly List<double?> _values = new();

    public Sparkline()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(27, 38, 55);
        LineColor = Color.FromArgb(69, 212, 196);
        FillColor = Color.FromArgb(40, 69, 212, 196);
        GridColor = Color.FromArgb(45, 61, 81);
    }

    public Color LineColor { get; set; }
    public Color FillColor { get; set; }
    public Color GridColor { get; set; }
    public string Unit { get; set; } = string.Empty;

    public void SetSeries(IEnumerable<double?> values)
    {
        _values.Clear();
        _values.AddRange(values);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var grid = new Pen(GridColor))
        {
            for (int i = 1; i < 4; i++)
            {
                int y = Height * i / 4;
                g.DrawLine(grid, 6, y, Width - 6, y);
            }
        }

        var present = _values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (present.Count < 2)
        {
            using var muted = new SolidBrush(Color.FromArgb(165, 183, 202));
            g.DrawString("sin datos", Font, muted, 8, Height / 2f - 8);
            return;
        }

        double min = present.Min();
        double max = present.Max();
        if (Math.Abs(max - min) < 0.001) { max += 1; min -= 1; }
        double range = max - min;
        float stepX = (Width - 12f) / (_values.Count - 1);
        var points = new List<PointF>();
        for (int i = 0; i < _values.Count; i++)
        {
            if (_values[i] is not { } value) continue;
            float x = 6 + stepX * i;
            float y = (float)(Height - 6 - (value - min) / range * (Height - 12));
            points.Add(new PointF(x, y));
        }

        if (points.Count >= 2)
        {
            using var fill = new SolidBrush(FillColor);
            var polygon = new List<PointF>(points) { new(points[^1].X, Height - 4), new(points[0].X, Height - 4) };
            g.FillPolygon(fill, polygon.ToArray());
            using var pen = new Pen(LineColor, 2f);
            g.DrawLines(pen, points.ToArray());
        }

        using var text = new SolidBrush(Color.FromArgb(232, 242, 252));
        string caption = Math.Round(min) + "–" + Math.Round(max) + (Unit.Length > 0 ? " " + Unit : string.Empty);
        g.DrawString(caption, Font, text, 8, 4);
    }
}
