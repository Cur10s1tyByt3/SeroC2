using System.Windows;
using System.Windows.Media.Animation;

namespace SeroServer.UI;

public partial class NotificationPopup : Window
{
    private const double DisplayMs = 3800;
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
        var area = SystemParameters.WorkArea;
        Left = area.Right  - Width  - 16;
        Top  = area.Bottom - Height - 16;

        lock (_active)
        {
            foreach (var p in _active)
                p.ShiftUp(Height + Spacing);
            _active.Add(this);
        }

        // Slide up + fade in
        Opacity = 0;
        var easeOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(FadeInMs)) { EasingFunction = easeOut });
        SlideTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(SlideInPx, 0, TimeSpan.FromMilliseconds(FadeInMs + 40)) { EasingFunction = easeOut });

        // Progress bar depletes linearly
        ProgressBar.BeginAnimation(WidthProperty,
            new DoubleAnimation(304, 0, TimeSpan.FromMilliseconds(DisplayMs)));

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
