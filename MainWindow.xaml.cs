using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace XrayDesktop;

public partial class MainWindow : Window
{
    private const string Img1Url = "https://xras.ru/image/xray_RAL5.png";
    private const string Img2Url = "https://xras.ru/image/kp_RAL5.png";
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XrayDesktop");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");
    private static readonly string CacheDir = Path.Combine(SettingsDir, "cache");

    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private readonly HttpClient _http = new();
    private Settings _settings = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetupTray();
        LoadSettings();
        ApplySettings();
        await LoadImagesAsync();
    }

    private void SetupTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Показать", null, (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; }));
        menu.Items.Add("Скрыть", null, (_, _) => Dispatcher.Invoke(Hide));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(() =>
        {
            SaveSettings();
            _trayIcon?.Dispose();
            WpfApp.Current.Shutdown();
        }));

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Visible = true,
            Text = "XrayDesktop",
            Icon = CreateTrayIcon(),
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; });
    }

    private async Task LoadImagesAsync()
    {
        Directory.CreateDirectory(CacheDir);
        try
        {
            var path1 = await DownloadOrCacheAsync(Img1Url, "xray_RAL5.png");
            var path2 = await DownloadOrCacheAsync(Img2Url, "kp_RAL5.png");
            Image1.Source = new BitmapImage(new Uri(path1));
            Image2.Source = new BitmapImage(new Uri(path2));
        }
        catch (Exception ex)
        {
            WpfMsg.Show($"Ошибка загрузки изображений: {ex.Message}", "XrayDesktop",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task<string> DownloadOrCacheAsync(string url, string filename)
    {
        var path = Path.Combine(CacheDir, filename);
        if (!File.Exists(path))
        {
            var data = await _http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(path, data);
        }
        return path;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var opacityItem = new System.Windows.Controls.MenuItem { Header = "Прозрачность" };
        var opacitySlider = new System.Windows.Controls.Slider
        {
            Minimum = 0.1,
            Maximum = 1.0,
            Width = 150,
            Value = Opacity,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true
        };
        opacitySlider.ValueChanged += (_, ev) => Opacity = ev.NewValue;
        opacityItem.Items.Add(opacitySlider);
        menu.Items.Add(opacityItem);

        var sizeItem = new System.Windows.Controls.MenuItem { Header = "Размер" };
        var sizeSlider = new System.Windows.Controls.Slider
        {
            Minimum = 100,
            Maximum = 800,
            Width = 150,
            Value = _settings.ImageWidth,
            TickFrequency = 50,
            IsSnapToTickEnabled = true
        };
        sizeSlider.ValueChanged += (_, ev) =>
        {
            _settings.ImageWidth = ev.NewValue;
            Image1.Width = ev.NewValue;
            Image2.Width = ev.NewValue;
        };
        sizeItem.Items.Add(sizeSlider);
        menu.Items.Add(sizeItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var saveItem = new System.Windows.Controls.MenuItem { Header = "Сохранить положение" };
        saveItem.Click += (_, _) => SaveSettings();
        menu.Items.Add(saveItem);

        var autostartItem = new System.Windows.Controls.MenuItem
        {
            Header = "Автозапуск",
            IsCheckable = true,
            IsChecked = IsAutostartEnabled()
        };
        autostartItem.Click += (_, _) => ToggleAutostart(autostartItem.IsChecked);
        menu.Items.Add(autostartItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Выход" };
        exitItem.Click += (_, _) =>
        {
            SaveSettings();
            _trayIcon?.Dispose();
            WpfApp.Current.Shutdown();
        };
        menu.Items.Add(exitItem);

        menu.IsOpen = true;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            Hide();
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(SettingsDir);
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Opacity = Opacity;
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(_settings, options));
    }

    private void LoadSettings()
    {
        if (File.Exists(SettingsFile))
        {
            try
            {
                _settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile)) ?? new Settings();
            }
            catch
            {
                _settings = new Settings();
            }
        }
    }

    private void ApplySettings()
    {
        Left = _settings.Left;
        Top = _settings.Top;
        Opacity = _settings.Opacity;
        Image1.Width = _settings.ImageWidth;
        Image2.Width = _settings.ImageWidth;
    }

    private bool IsAutostartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("XrayDesktop") != null;
        }
        catch { return false; }
    }

    private void ToggleAutostart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath != null)
                    key.SetValue("XrayDesktop", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("XrayDesktop", false);
            }
        }
        catch (Exception ex)
        {
            WpfMsg.Show($"Ошибка автозапуска: {ex.Message}", "XrayDesktop",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        var bmp = new System.Drawing.Bitmap(32, 32);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);

        using var bodyBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new System.Drawing.Rectangle(0, 0, 32, 32),
            System.Drawing.Color.FromArgb(60, 120, 215),
            System.Drawing.Color.FromArgb(30, 70, 160),
            System.Drawing.Drawing2D.LinearGradientMode.ForwardDiagonal);
        using var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(20, 50, 120), 1.2f);

        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(1, 1, 28, 28, 180, 90);
        path.AddArc(18, 1, 12, 12, 270, 90);
        path.AddArc(18, 18, 12, 12, 0, 90);
        path.AddArc(1, 18, 12, 12, 90, 90);
        path.CloseAllFigures();

        g.FillPath(bodyBrush, path);
        g.DrawPath(borderPen, path);

        using var crossPen = new System.Drawing.Pen(System.Drawing.Color.White, 2.8f);
        crossPen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
        crossPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
        g.DrawLine(crossPen, 10, 8, 10, 24);
        g.DrawLine(crossPen, 8, 10, 24, 10);

        using var glow = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(90, 255, 255, 255));
        g.FillEllipse(glow, 5, 4, 8, 8);

        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveSettings();
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }
}

public class Settings
{
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    public double Opacity { get; set; } = 0.9;
    public double ImageWidth { get; set; } = 300;
}
