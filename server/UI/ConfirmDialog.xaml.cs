using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SeroServer.UI;

public partial class ConfirmDialog : Window
{
    /// <param name="icon">Optional Segoe Fluent Icons glyph (e.g. "" for trash).</param>
    /// <param name="iconColor">Optional hex color for the icon (e.g. "#E04040"). Defaults to TitleTextBrush.</param>
    public ConfirmDialog(string title, string message,
                         string yesLabel = "YES", string noLabel = "NO",
                         string? icon = null, string? iconColor = null)
    {
        InitializeComponent();
        TxtTitle.Text   = title;
        TxtMessage.Text = message;
        BtnYes.Content  = yesLabel.ToUpper();
        BtnNo.Content   = noLabel.ToUpper();

        if (icon != null)
        {
            IconGlyph.Text = icon;
            if (iconColor != null)
            {
                try
                {
                    IconGlyph.Foreground = new SolidColorBrush(
                        (Color)System.Windows.Media.ColorConverter.ConvertFromString(iconColor));
                }
                catch { /* keep default */ }
            }
            else
            {
                IconGlyph.SetResourceReference(ForegroundProperty, "TitleTextBrush");
            }
            IconGlyph.Visibility = Visibility.Visible;
        }
    }

    private void Yes_Click(object s, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void No_Click (object s, RoutedEventArgs e) => Close();

    private void Window_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
