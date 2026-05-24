using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ARCYN.UI.Services;

public sealed class AnimationService
{
    public static void FadeIn(UIElement target, double durationMs = 120, double from = 0, double to = 1, Action? onComplete = null)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        if (onComplete != null)
            anim.Completed += (_, _) => onComplete();
        target.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    public static void FadeOut(UIElement target, double durationMs = 100, Action? onComplete = null)
    {
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        if (onComplete != null)
            anim.Completed += (_, _) => onComplete();
        target.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    public static void ScaleTo(FrameworkElement target, double toX, double toY, double durationMs, EasingMode mode = EasingMode.EaseOut)
    {
        var animX = new DoubleAnimation(toX, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = mode }
        };
        var animY = new DoubleAnimation(toY, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = mode }
        };
        target.RenderTransform ??= new ScaleTransform(1, 1);
        if (target.RenderTransform is ScaleTransform st)
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
        }
    }

    public static void ResizeTo(FrameworkElement target, double? toWidth, double? toHeight, double durationMs, EasingMode mode = EasingMode.EaseOut)
    {
        if (toWidth.HasValue)
        {
            var anim = new DoubleAnimation(toWidth.Value, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = new CubicEase { EasingMode = mode }
            };
            target.BeginAnimation(FrameworkElement.WidthProperty, anim);
        }
        if (toHeight.HasValue)
        {
            var anim = new DoubleAnimation(toHeight.Value, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = new CubicEase { EasingMode = mode }
            };
            target.BeginAnimation(FrameworkElement.HeightProperty, anim);
        }
    }
}
