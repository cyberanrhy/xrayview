using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace XrayDesktop;

public partial class MainWindow : Window
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XrayDesktop");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");
    private static readonly string CacheDir = Path.Combine(SettingsDir, "cache");

    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Windows.Threading.DispatcherTimer? _refreshTimer;
    private readonly HttpClient _http = new();
    private Settings _settings = new();
    private readonly List<System.Windows.Controls.Image> _imageControls = new();

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
        await LoadAllImagesAsync();
        StartRefreshTimer();
    }

    private void StartRefreshTimer()
    {
        _refreshTimer?.Stop();
        _refreshTimer = new System.Windows.Threading.DispatcherTimer();
        _refreshTimer.Interval = TimeSpan.FromMinutes(_settings.RefreshInterval);
        _refreshTimer.Tick += async (_, _) => await RefreshAllImagesAsync();
        _refreshTimer.Start();
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

    private void BuildImageStack()
    {
        ImageStack.Children.Clear();
        _imageControls.Clear();
        foreach (var url in _settings.ImageUrls)
        {
            var img = new System.Windows.Controls.Image
            {
                Stretch = System.Windows.Media.Stretch.Uniform,
                Width = _settings.ImageWidth
            };
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.HighQuality);
            _imageControls.Add(img);
            ImageStack.Children.Add(img);
        }
    }

    private async Task LoadAllImagesAsync()
    {
        Directory.CreateDirectory(CacheDir);
        BuildImageStack();

        for (int i = 0; i < _settings.ImageUrls.Count; i++)
        {
            try
            {
                var path = await DownloadOrCacheAsync(_settings.ImageUrls[i], $"img_{i}.png");
                _imageControls[i].Source = new BitmapImage(new Uri(path));
            }
            catch { }
        }
    }

    private async Task RefreshAllImagesAsync()
    {
        try
        {
            for (int i = 0; i < _settings.ImageUrls.Count && i < _imageControls.Count; i++)
            {
                var data = await _http.GetByteArrayAsync(_settings.ImageUrls[i]);
                await File.WriteAllBytesAsync(Path.Combine(CacheDir, $"img_{i}.png"), data);
                _imageControls[i].Source = LoadImage(data);
            }
        }
        catch { }
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

    private static BitmapImage LoadImage(byte[] data)
    {
        var img = new BitmapImage();
        using var ms = new MemoryStream(data);
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var refreshItem = new System.Windows.Controls.MenuItem { Header = "Обновить сейчас" };
        refreshItem.Click += async (_, _) => await RefreshAllImagesAsync();
        menu.Items.Add(refreshItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var intervalItem = new System.Windows.Controls.MenuItem { Header = $"Интервал обновления ({_settings.RefreshInterval} мин)" };
        var intervalSlider = new System.Windows.Controls.Slider
        {
            Minimum = 1, Maximum = 60, Width = 150,
            Value = _settings.RefreshInterval,
            TickFrequency = 1, IsSnapToTickEnabled = true
        };
        intervalSlider.ValueChanged += (_, ev) =>
        {
            _settings.RefreshInterval = (int)ev.NewValue;
            intervalItem.Header = $"Интервал обновления ({_settings.RefreshInterval} мин)";
            StartRefreshTimer();
        };
        intervalItem.Items.Add(intervalSlider);
        menu.Items.Add(intervalItem);

        var opacityItem = new System.Windows.Controls.MenuItem { Header = "Прозрачность" };
        var opacitySlider = new System.Windows.Controls.Slider
        {
            Minimum = 0.1, Maximum = 1.0, Width = 150,
            Value = Opacity, TickFrequency = 0.05, IsSnapToTickEnabled = true
        };
        opacitySlider.ValueChanged += (_, ev) => Opacity = ev.NewValue;
        opacityItem.Items.Add(opacitySlider);
        menu.Items.Add(opacityItem);

        var sizeItem = new System.Windows.Controls.MenuItem { Header = "Размер" };
        var sizeSlider = new System.Windows.Controls.Slider
        {
            Minimum = 100, Maximum = 800, Width = 150,
            Value = _settings.ImageWidth, TickFrequency = 50, IsSnapToTickEnabled = true
        };
        sizeSlider.ValueChanged += (_, ev) =>
        {
            _settings.ImageWidth = ev.NewValue;
            foreach (var img in _imageControls) img.Width = ev.NewValue;
        };
        sizeItem.Items.Add(sizeSlider);
        menu.Items.Add(sizeItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var imagesItem = new System.Windows.Controls.MenuItem { Header = $"Картинки ({_settings.ImageUrls.Count})" };

        for (int i = 0; i < _settings.ImageUrls.Count; i++)
        {
            var idx = i;
            var name = GetImageShortName(_settings.ImageUrls[idx]);
            var subItem = new System.Windows.Controls.MenuItem { Header = $"{idx + 1}. {name}" };

            var changeItem = new System.Windows.Controls.MenuItem { Header = "Заменить..." };
            changeItem.Click += async (_, _) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = $"Выберите картинку {idx + 1}", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All|*.*" };
                if (dialog.ShowDialog() == true)
                {
                    _settings.ImageUrls[idx] = dialog.FileName;
                    await ReloadImageAsync(idx);
                }
            };
            subItem.Items.Add(changeItem);

            var removeItem = new System.Windows.Controls.MenuItem { Header = "Удалить" };
            removeItem.Click += async (_, _) =>
            {
                if (_settings.ImageUrls.Count <= 1) return;
                _settings.ImageUrls.RemoveAt(idx);
                await LoadAllImagesAsync();
            };
            subItem.Items.Add(removeItem);

            imagesItem.Items.Add(subItem);
        }

        var addImageItem = new System.Windows.Controls.MenuItem { Header = "Добавить картинку..." };
        addImageItem.Click += async (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Выберите картинку", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All|*.*" };
            if (dialog.ShowDialog() == true)
            {
                _settings.ImageUrls.Add(dialog.FileName);
                await LoadAllImagesAsync();
            }
        };
        imagesItem.Items.Add(addImageItem);

        var resetUrlsItem = new System.Windows.Controls.MenuItem { Header = "Сбросить все URL" };
        resetUrlsItem.Click += async (_, _) =>
        {
            _settings.ImageUrls = new List<string>
            {
                "https://xras.ru/image/xray_RAL5.png",
                "https://xras.ru/image/kp_RAL5.png"
            };
            await LoadAllImagesAsync();
        };
        imagesItem.Items.Add(resetUrlsItem);

        menu.Items.Add(imagesItem);

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

    private async Task ReloadImageAsync(int index)
    {
        try
        {
            var data = await _http.GetByteArrayAsync(_settings.ImageUrls[index]);
            await File.WriteAllBytesAsync(Path.Combine(CacheDir, $"img_{index}.png"), data);
            if (index < _imageControls.Count)
                _imageControls[index].Source = LoadImage(data);
        }
        catch { }
    }

    private static string GetImageShortName(string url)
    {
        try
        {
            var name = Path.GetFileName(new Uri(url).AbsolutePath);
            return name.Length > 30 ? name[..27] + "..." : name;
        }
        catch
        {
            return url.Length > 30 ? url[..27] + "..." : url;
        }
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

        if (_settings.ImageUrls == null || _settings.ImageUrls.Count == 0)
        {
            _settings.ImageUrls = new List<string>
            {
                "https://xras.ru/image/xray_RAL5.png",
                "https://xras.ru/image/kp_RAL5.png"
            };
        }
    }

    private void ApplySettings()
    {
        Left = _settings.Left;
        Top = _settings.Top;
        Opacity = _settings.Opacity;
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
        _refreshTimer?.Stop();
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
    public int RefreshInterval { get; set; } = 20;
    public List<string> ImageUrls { get; set; } = new();
}
