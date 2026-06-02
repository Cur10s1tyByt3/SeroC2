using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SeroServer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // WPF hardware acceleration (DirectX) produces random color artifacts on Hyper-V
        // and any basic/virtual display adapter (BlurEffect, DropShadowEffect, animated
        // gradients all trigger DirectX re-renders that fail silently on Basic Display Adapter).
        // Software rendering avoids all GPU compositing — CPU cost is negligible for a UI-only app.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
    }
}
