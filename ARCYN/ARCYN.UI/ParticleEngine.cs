using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ARCYN.UI;

public class ParticleEngine
{
    private readonly Canvas _canvas;
    private readonly Random _rng = new();
    private readonly List<Particle> _particles = [];
    private DispatcherTimer? _timer;
    private const int Count = 40;

    private class Particle
    {
        public Ellipse Shape { get; set; } = new();
        public double Vx { get; set; }
        public double Vy { get; set; }
        public double Life { get; set; }
        public double MaxLife { get; set; }
        public double DriftPhase { get; set; }
        public double DriftAmp { get; set; }
    }

    public ParticleEngine(Canvas canvas) => _canvas = canvas;

    public void Start()
    {
        for (int i = 0; i < Count; i++)
        {
            var size = _rng.Next(2, 5);
            var isBright = _rng.NextDouble() > 0.75;
            var color = isBright
                ? Color.FromRgb(0x8E, 0xCC, 0xDB)
                : Color.FromRgb(0x5F, 0xA3, 0xB3);

            var p = new Particle
            {
                Shape = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(Color.FromArgb(
                        (byte)_rng.Next(40, 180), color.R, color.G, color.B)),
                    Opacity = 0
                },
                Vx = (_rng.NextDouble() - 0.5) * 0.8,
                Vy = -(_rng.NextDouble() * 0.5 + 0.12),
                MaxLife = _rng.Next(120, 350),
                Life = _rng.Next(-60, 0),
                DriftPhase = _rng.NextDouble() * Math.PI * 2,
                DriftAmp = _rng.NextDouble() * 0.5 + 0.15
            };

            Canvas.SetLeft(p.Shape, _rng.NextDouble() * 800);
            Canvas.SetTop(p.Shape, _rng.NextDouble() * 480);
            Panel.SetZIndex(p.Shape, 0);
            _canvas.Children.Add(p.Shape);
            _particles.Add(p);
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += Update;
        _timer.Start();

        _canvas.SizeChanged += (_, _) => { };
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
        _canvas.Children.Clear();
        _particles.Clear();
    }

    private void Update(object? sender, EventArgs e)
    {
        if (!_canvas.IsVisible) return;
        var w = Math.Max(_canvas.ActualWidth, 800);
        var h = Math.Max(_canvas.ActualHeight, 480);

        foreach (var p in _particles)
        {
            p.Life++;
            if (p.Life < 0) continue;

            var ratio = p.Life / p.MaxLife;
            if (ratio > 1) ratio = 1;

            if (ratio < 0.08) p.Shape.Opacity = ratio / 0.08;
            else if (ratio > 0.75) p.Shape.Opacity = (1 - ratio) / 0.25;
            else p.Shape.Opacity = 1;

            var x = Canvas.GetLeft(p.Shape) + p.Vx;
            var y = Canvas.GetTop(p.Shape) + p.Vy;

            x += Math.Sin(p.Life * 0.02 + p.DriftPhase) * p.DriftAmp;

            if (y < -10 || x < -10 || x > w + 10 || p.Life > p.MaxLife)
            {
                Canvas.SetLeft(p.Shape, _rng.NextDouble() * w);
                Canvas.SetTop(p.Shape, h + 5);
                p.Vx = (_rng.NextDouble() - 0.5) * 0.8;
                p.Vy = -(_rng.NextDouble() * 0.5 + 0.12);
                p.Life = 0;
                p.MaxLife = _rng.Next(120, 350);
                continue;
            }

            Canvas.SetLeft(p.Shape, x);
            Canvas.SetTop(p.Shape, y);
        }
    }
}
