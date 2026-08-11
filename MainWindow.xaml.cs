using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Stm32SerialLab;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        FitWindowToCurrentDisplay();

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    private void FitWindowToCurrentDisplay()
    {
        DisplayArea display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        RectInt32 workArea = display.WorkArea;
        int width = Math.Min(1180, Math.Max(720, workArea.Width - 48));
        int height = Math.Min(780, Math.Max(520, workArea.Height - 48));
        int x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        int y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }
}
