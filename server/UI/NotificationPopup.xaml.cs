using System.Windows;
using System.Windows.Media.Animation;

namespace SeroServer.UI;

public partial class NotificationPopup : Window
{
    private const double DisplayMs = 2000;
    private const double FadeInMs  = 200;
    private const double FadeOutMs = 280;
    private const double SlideInPx = 20;
    private const double Spacing   = 8;

    private static readonly List<NotificationPopup> _active = [];

    public NotificationPopup(string title, string body)
    {
        InitializeComponent();
        TxtTitle.Text = title;
        TxtBody.Text  = body;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object s, RoutedEventArgs e)
    {
        var area     = SystemParameters.WorkArea;
        double finalTop = area.Bottom - Height - 16;
        Left = area.Right - Width - 16;
        Top  = finalTop + SlideInPx; // start slightly below

        lock (_active)
        {
            foreach (var p in _active)
                p.ShiftUp(Height + Spacing);
            _active.Add(this);
        }

        // Fade in
        Opacity = 0;
        var easeOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(FadeInMs)) { EasingFunction = easeOut });

        // Slide up via Top storyboard (RenderTransform is invalid on Window)
        var sbSlide = new Storyboard();
        var slideAnim = new DoubleAnimation(finalTop + SlideInPx, finalTop, TimeSpan.FromMilliseconds(FadeInMs + 40))
            { EasingFunction = easeOut };
        Storyboard.SetTarget(slideAnim, this);
        Storyboard.SetTargetProperty(slideAnim, new PropertyPath(TopProperty));
        sbSlide.Children.Add(slideAnim);
        sbSlide.Begin();

        // Progress bar depletes linearly
        ProgressBar.BeginAnimation(WidthProperty,
            new DoubleAnimation(264, 0, TimeSpan.FromMilliseconds(DisplayMs)));

        var timer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(DisplayMs) };
        timer.Tick += (_, _) => { timer.Stop(); FadeOut(); };
        timer.Start();
    }

    private void ShiftUp(double amount)
    {
        var sb   = new Storyboard();
        var anim = new DoubleAnimation(Top, Top - amount, TimeSpan.FromMilliseconds(180))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(anim, this);
        Storyboard.SetTargetProperty(anim, new PropertyPath(TopProperty));
        sb.Children.Add(anim);
        sb.Begin();
    }

    private void FadeOut()
    {
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(FadeOutMs))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) =>
        {
            lock (_active) _active.Remove(this);
            Close();
        };
        BeginAnimation(OpacityProperty, fade);
    }
}
