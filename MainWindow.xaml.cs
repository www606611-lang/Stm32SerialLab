using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Stm32SerialLab.Services;
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
    private readonly AppSettingsService _settings = AppSettingsService.Current;

    public MainWindow()
    {
        InitializeComponent();

        WindowRoot.RequestedTheme = ParseTheme(_settings.Values.Theme);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        RestoreWindowPlacement();
        Closed += MainWindow_Closed;

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    public ElementTheme CurrentTheme => WindowRoot.ActualTheme;

    public void SetTheme(ElementTheme theme)
    {
        WindowRoot.RequestedTheme = theme;
        _settings.Values.Theme = theme.ToString();
        _settings.Save();
    }

    private static ElementTheme ParseTheme(string value)
    {
        return Enum.TryParse(value, true, out ElementTheme theme) &&
                   theme is ElementTheme.Light or ElementTheme.Dark
            ? theme
            : ElementTheme.Default;
    }

    private void RestoreWindowPlacement()
    {
        AppSettings settings = _settings.Values;
        if (settings.WindowX is not int savedX ||
            settings.WindowY is not int savedY ||
            settings.WindowWidth is not int savedWidth ||
            settings.WindowHeight is not int savedHeight)
        {
            FitWindowToCurrentDisplay();
            return;
        }

        DisplayArea display = DisplayArea.GetFromPoint(
            new PointInt32(savedX, savedY),
            DisplayAreaFallback.Nearest);
        RectInt32 workArea = display.WorkArea;
        int width = Math.Clamp(savedWidth, Math.Min(640, workArea.Width), workArea.Width);
        int height = Math.Clamp(savedHeight, Math.Min(480, workArea.Height), workArea.Height);
        int x = Math.Clamp(savedX, workArea.X, workArea.X + workArea.Width - width);
        int y = Math.Clamp(savedY, workArea.Y, workArea.Y + workArea.Height - height);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));

        if (settings.WindowMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        AppSettings settings = _settings.Values;
        settings.WindowX = AppWindow.Position.X;
        settings.WindowY = AppWindow.Position.Y;
        settings.WindowWidth = AppWindow.Size.Width;
        settings.WindowHeight = AppWindow.Size.Height;
        settings.WindowMaximized = AppWindow.Presenter is OverlappedPresenter presenter &&
                                   presenter.State == OverlappedPresenterState.Maximized;
        _settings.Save();
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
