using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace InkGuide.App.Hosting;

/// <summary>
/// Persists and restores window size/position/maximized state with
/// multi-monitor sanity checks: a stored position that no longer intersects
/// the virtual screen is discarded in favour of centred defaults.
/// </summary>
internal sealed class WindowStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<WindowStateStore> _logger;
    private readonly string _filePath;

    public WindowStateStore(ILogger<WindowStateStore> logger, AppPaths paths)
    {
        _logger = logger;
        _filePath = paths.WindowStatePath;
    }

    public void Restore(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        WindowStateDto? saved = Load();
        if (saved is null)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        double virtualLeft = SystemParameters.VirtualScreenLeft;
        double virtualTop = SystemParameters.VirtualScreenTop;
        double virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        double virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        bool intersects = saved.Left < virtualRight && saved.Left + saved.Width > virtualLeft &&
                          saved.Top < virtualBottom && saved.Top + saved.Height > virtualTop;
        if (!intersects || saved.Width < 200 || saved.Height < 150)
        {
            AppLogMessages.OffscreenPosition(_logger);
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = saved.Left;
        window.Top = saved.Top;
        window.Width = saved.Width;
        window.Height = saved.Height;
        if (saved.Maximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    public void Save(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            var dto = new WindowStateDto
            {
                Left = window.RestoreBounds.Left,
                Top = window.RestoreBounds.Top,
                Width = window.RestoreBounds.Width,
                Height = window.RestoreBounds.Height,
                Maximized = window.WindowState == WindowState.Maximized,
            };
            string json = JsonSerializer.Serialize(dto, JsonOptions);
            string temp = _filePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            AppLogMessages.PersistFailed(_logger, ex);
        }
    }

    private WindowStateDto? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<WindowStateDto>(json);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
        {
            AppLogMessages.ReadFailed(_logger, ex);
            return null;
        }
    }

    private sealed class WindowStateDto
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }
}
