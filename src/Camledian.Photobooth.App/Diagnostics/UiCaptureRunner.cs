using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Camledian.Photobooth.App.ViewModels;
using Camledian.Photobooth.Core.StateMachine;

namespace Camledian.Photobooth.App.Diagnostics;

/// <summary>
/// Screenshots every kiosk screen and writes them to a directory, then exits. Driven by the
/// <c>--ui-capture &lt;dir&gt;</c> command line switch (see <see cref="App.OnStartup"/>).
///
/// Exists because the kiosk UI cannot be looked at from the Linux devcontainer the app is normally
/// developed in — WPF only renders on Windows. This runs on the <c>windows-latest</c> CI runner
/// instead, uploads the PNGs as a workflow artifact, and so gives a way to actually review a XAML
/// change without a Windows machine in reach.
///
/// It drives the real <see cref="MainWindow"/> with the real DI-built <see cref="MainViewModel"/>
/// and forces kiosk mode on, so what is captured is the genuine fullscreen chrome
/// (<c>WindowStyle.None</c>, maximized, topmost) — not a stand-in rendered in isolation. States are
/// set directly rather than driven through <see cref="PhotoboothStateMachine"/>: the goal is to see
/// each screen, not to exercise the transitions, which the unit tests already cover.
/// </summary>
internal static class UiCaptureRunner
{
    public const string Flag = "--ui-capture";

    /// <summary>
    /// Screens worth a picture. <c>Capturing</c> is skipped — it renders the same LiveView as
    /// <c>Countdown</c> — and Admin appears twice because the locked PIN gate and the unlocked
    /// settings form are entirely different layouts.
    /// </summary>
    private static readonly (string Name, PhotoboothState State, bool AdminUnlocked)[] Screens =
    [
        ("idle", PhotoboothState.Idle, false),
        ("select-background", PhotoboothState.SelectingBackground, false),
        ("live-preview", PhotoboothState.Preview, false),
        ("live-countdown", PhotoboothState.Countdown, false),
        ("select-photo", PhotoboothState.SelectingPhoto, false),
        ("processing", PhotoboothState.Processing, false),
        ("result", PhotoboothState.Result, false),
        ("error", PhotoboothState.Error, false),
        ("admin-locked", PhotoboothState.Admin, false),
        ("admin-unlocked", PhotoboothState.Admin, true),
    ];

    public static async Task RunAsync(Window window, MainViewModel viewModel, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        // Force kiosk chrome regardless of what the stored settings say, so the captures always show
        // the mode the photobooth actually runs in at an event.
        viewModel.IsKioskMode = true;

        // Screens that would otherwise render an empty frame, because their images normally come
        // from a camera and the imaging pipeline.
        viewModel.PreviewImage = CreatePlaceholder(1280, 720, Color.FromRgb(0x2E, 0x7D, 0x32));
        viewModel.ResultImage = CreatePlaceholder(1280, 720, Color.FromRgb(0x37, 0x47, 0x4F));
        viewModel.CountdownValue = 3;
        viewModel.ErrorMessage = "Ukázková chybová hláška pro kontrolu vzhledu.";

        await SettleAsync(window);

        foreach (var (name, state, adminUnlocked) in Screens)
        {
            viewModel.State = state;
            viewModel.IsAdminUnlocked = adminUnlocked;
            await SettleAsync(window);

            Save(window, Path.Combine(outputDirectory, $"{name}.png"));
        }

        // The Admin screen is where the WPF UI migration carries the most risk — every stock control
        // it uses (TextBox/ComboBox/Slider/CheckBox/TabItem) got its template swapped — so each tab
        // gets its own capture rather than just whichever one happens to open first.
        await CaptureAdminTabsAsync(window, viewModel, outputDirectory);
    }

    private static async Task CaptureAdminTabsAsync(Window window, MainViewModel viewModel, string outputDirectory)
    {
        viewModel.State = PhotoboothState.Admin;
        viewModel.IsAdminUnlocked = true;
        await SettleAsync(window);

        if (FindDescendant<TabControl>(window) is not { } tabControl)
        {
            return;
        }

        for (var i = 0; i < tabControl.Items.Count; i++)
        {
            tabControl.SelectedIndex = i;
            await SettleAsync(window);

            var header = (tabControl.Items[i] as TabItem)?.Header?.ToString() ?? $"tab{i}";
            Save(window, Path.Combine(outputDirectory, $"admin-{i:00}-{Slug(header)}.png"));
        }
    }

    /// <summary>
    /// Lets WPF finish measure/arrange/render for the pending change. Rendering happens below
    /// <see cref="DispatcherPriority.Loaded"/>, so yielding at that priority is what guarantees the
    /// visual tree is current before <see cref="RenderTargetBitmap"/> walks it — a plain delay would
    /// be a guess.
    /// </summary>
    private static async Task SettleAsync(Window window)
    {
        window.UpdateLayout();
        await Task.Delay(150);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
    }

    private static void Save(Visual visual, string path)
    {
        var bounds = VisualTreeHelper.GetDescendantBounds(visual);
        var width = (int)Math.Ceiling(bounds.Width);
        var height = (int)Math.Ceiling(bounds.Height);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static BitmapSource CreatePlaceholder(int width, int height, Color color)
    {
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return bitmap;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static string Slug(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }
}
