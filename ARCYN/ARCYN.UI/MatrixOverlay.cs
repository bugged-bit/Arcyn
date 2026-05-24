using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ARCYN.UI;

public class MatrixOverlay
{
    private readonly Canvas _canvas;
    private readonly Random _rng = new();
    private readonly List<MatrixColumn> _columns = [];
    private DispatcherTimer? _timer;
    private const string HexChars = "0123456789ABCDEF";
    private int _tickCount;
    private const int FlashInterval = 25;

    private class MatrixColumn
    {
        public TextBlock Label { get; set; } = new();
        public double Speed { get; set; }
        public double Pos { get; set; }
        public int Length { get; set; }
        public double FlashTime { get; set; } = -1;
    }

    public MatrixOverlay(Canvas canvas)
    {
        _canvas = canvas;
    }

    public void Start()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _timer.Tick += Update;
        _timer.Start();

        _canvas.SizeChanged += (_, _) => InitColumns();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
        _canvas.Children.Clear();
        _columns.Clear();
    }

    private void InitColumns()
    {
        _canvas.Children.Clear();
        _columns.Clear();

        var w = Math.Max(_canvas.ActualWidth, 800);
        var h = Math.Max(_canvas.ActualHeight, 450);
        var colW = 14;
        var colCount = (int)(w / colW);

        for (int i = 0; i < colCount; i++)
        {
            var len = _rng.Next(4, 12);
            var tb = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(20, 0xFF, 0xFF, 0xFF)),
                Opacity = 0
            };

            var chars = new char[len];
            for (int j = 0; j < len; j++)
                chars[j] = HexChars[_rng.Next(16)];
            tb.Text = new string(chars);

            Canvas.SetLeft(tb, i * colW);
            Canvas.SetTop(tb, _rng.NextDouble() * h);

            _ = _canvas.Dispatcher.BeginInvoke(() =>
            {
                Panel.SetZIndex(tb, -10);
                _canvas.Children.Add(tb);
            });

            _columns.Add(new MatrixColumn
            {
                Label = tb,
                Speed = _rng.NextDouble() * 0.8 + 0.3,
                Pos = _rng.NextDouble() * h,
                Length = len
            });
        }
    }

    private void Update(object? sender, EventArgs e)
    {
        if (!_canvas.IsVisible) return;

        var h = Math.Max(_canvas.ActualHeight, 450);

        _tickCount++;
        if (_tickCount >= FlashInterval && _columns.Count > 0)
        {
            _tickCount = 0;
            var col = _columns[_rng.Next(_columns.Count)];
            col.FlashTime = 0;
        }

        foreach (var col in _columns)
        {
            col.Pos += col.Speed;
            if (col.Pos > h + col.Length * 14)
            {
                col.Pos = -col.Length * 14;
                var chars = new char[col.Length];
                for (int i = 0; i < col.Length; i++)
                    chars[i] = HexChars[_rng.Next(16)];
                col.Label.Text = new string(chars);
            }

            var topRatio = col.Pos / h;
            double baseOpacity;
            if (topRatio < 0.15)
                baseOpacity = topRatio / 0.15 * 0.6;
            else if (topRatio > 0.7)
                baseOpacity = (1 - topRatio) / 0.3 * 0.6;
            else
                baseOpacity = 0.6;

            if (col.FlashTime >= 0)
            {
                col.FlashTime += 80;
                var flashProgress = col.FlashTime / 400.0;
                if (flashProgress >= 1)
                {
                    col.FlashTime = -1;
                    col.Label.Opacity = baseOpacity;
                }
                else
                {
                    col.Label.Opacity = baseOpacity + (1 - flashProgress) * (1 - baseOpacity);
                    col.Label.Foreground = new SolidColorBrush(Color.FromArgb(
                        (byte)(255 * (1 - flashProgress * 0.7)),
                        0x8E, 0xCC, 0xDB));
                }
            }
            else
            {
                col.Label.Opacity = baseOpacity;
            }

            Canvas.SetTop(col.Label, col.Pos);
        }
    }
}
