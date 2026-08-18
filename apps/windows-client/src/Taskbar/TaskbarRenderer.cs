using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Automation;
using LyricRelay.Core;

namespace LyricRelay.Windows;

public sealed class TaskbarRenderer : IDisposable
{
    private const long LayoutRefreshIntervalMs = 1000;
    private const long RetryLayoutIntervalMs = 250;
    private TaskbarOverlayWindow? _window;
    private TaskbarLayout? _cachedLayout;
    private long _layoutRefreshAt;
    private RenderKey? _lastRender;
    private bool _disposed;

    public void Render(TimedLine? current, TimedLine? next, AppSettings settings)
    {
        if (_disposed) return;

        if (!settings.ShowLyrics || current is null || string.IsNullOrWhiteSpace(current.Text))
        {
            Hide();
            return;
        }

        try
        {
            if (_window is null || _window.IsClosed)
            {
                _window = new TaskbarOverlayWindow();
                _cachedLayout = null;
                _lastRender = null;
            }

            var now = Environment.TickCount64;
            if (_cachedLayout is null || now >= _layoutRefreshAt)
            {
                _cachedLayout = TaskbarLayoutFinder.TryGet(_window.Handle);
                _layoutRefreshAt = now + (_cachedLayout is null
                    ? RetryLayoutIntervalMs
                    : LayoutRefreshIntervalMs);
            }

            if (_cachedLayout is null)
            {
                Hide();
                return;
            }

            var nextText = settings.DoubleLine ? next?.Text : null;
            var render = new RenderKey(
                current.Text,
                nextText,
                settings.FontFamily,
                settings.FontSize,
                settings.FontWeightValue,
                settings.Color,
                settings.Alignment);
            if (_lastRender == render)
            {
                return;
            }

            _window.Update(current.Text, nextText, settings, _cachedLayout.Value);
            _lastRender = render;
        }
        catch (InvalidOperationException)
        {
            ResetOverlay();
        }
        catch (Win32Exception)
        {
            ResetOverlay();
        }
        catch (COMException)
        {
            ResetOverlay();
        }
    }

    public void Hide()
    {
        _lastRender = null;
        if (!_disposed && _window is not null && !_window.IsClosed)
        {
            _window.HideOverlay();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        ResetOverlay();
    }

    private void ResetOverlay()
    {
        var window = _window;
        _window = null;
        _cachedLayout = null;
        _lastRender = null;
        if (window is null || window.IsClosed) return;
        try
        {
            window.Close();
        }
        catch (InvalidOperationException)
        {
            // The WPF window can be closed concurrently during application exit.
        }
    }

    private readonly record struct RenderKey(
        string Current,
        string? Next,
        string FontFamily,
        double FontSize,
        int FontWeight,
        string Color,
        string Alignment);
}

internal readonly record struct TaskbarLayout(
    IntPtr Handle,
    int Width,
    int Height,
    int ContentX,
    int ContentY,
    int ContentWidth,
    int ContentHeight,
    uint Dpi,
    bool Vertical,
    bool ReverseVertical);

internal static class TaskbarLayoutFinder
{
    public static TaskbarLayout? TryGet(IntPtr excludeHandle)
    {
        var handle = Native.FindWindow("Shell_TrayWnd", null);
        if (handle == IntPtr.Zero || !Native.GetWindowRect(handle, out var rect)) return null;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return null;
        var vertical = height > width;
        var occupied = FindUiAutomationOccupied(handle, rect, width, height);
        var content = FindPreferredGap(rect, occupied, vertical);
        if (content is null)
        {
            occupied = FindNativeOccupied(handle, rect, excludeHandle, width, height);
            content = FindPreferredGap(rect, occupied, vertical);
        }

        if (content is null) return null;
        var dpi = Native.GetDpiForWindow(handle);
        return new TaskbarLayout(
            handle,
            width,
            height,
            content.Value.Left - rect.Left,
            content.Value.Top - rect.Top,
            content.Value.Right - content.Value.Left,
            content.Value.Bottom - content.Value.Top,
            dpi == 0 ? 96u : dpi,
            vertical,
            rect.Left <= 2);
    }

    private static List<Native.Rect> FindUiAutomationOccupied(
        IntPtr taskbarHandle,
        Native.Rect taskbar,
        int taskbarWidth,
        int taskbarHeight)
    {
        var occupied = new List<Native.Rect>();
        try
        {
            var taskbarElement = AutomationElement.FromHandle(taskbarHandle);
            if (taskbarElement is null) return occupied;

            var elements = taskbarElement.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
            foreach (AutomationElement element in elements)
            {
                try
                {
                    if (element.Current.ProcessId == Environment.ProcessId) continue;
                    var bounds = element.Current.BoundingRectangle;
                    if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) continue;
                    var elementRect = new Native.Rect(
                        (int)Math.Floor(bounds.Left),
                        (int)Math.Floor(bounds.Top),
                        (int)Math.Ceiling(bounds.Right),
                        (int)Math.Ceiling(bounds.Bottom));
                    AddOccupied(occupied, elementRect, taskbar, taskbarWidth, taskbarHeight);
                }
                catch (ElementNotAvailableException)
                {
                    // The taskbar changes while Explorer is rebuilding its UI tree.
                }
            }
        }
        catch (ElementNotAvailableException)
        {
            return new List<Native.Rect>();
        }
        catch (COMException)
        {
            return new List<Native.Rect>();
        }

        return occupied;
    }

    private static List<Native.Rect> FindNativeOccupied(
        IntPtr taskbarHandle,
        Native.Rect taskbar,
        IntPtr excludeHandle,
        int taskbarWidth,
        int taskbarHeight)
    {
        var occupied = new List<Native.Rect>();
        Native.EnumChildWindows(taskbarHandle, (child, _) =>
        {
            if (child == excludeHandle || !Native.GetWindowRect(child, out var childRect)) return true;
            AddOccupied(occupied, childRect, taskbar, taskbarWidth, taskbarHeight);
            return true;
        }, IntPtr.Zero);
        return occupied;
    }

    private static void AddOccupied(
        List<Native.Rect> occupied,
        Native.Rect candidate,
        Native.Rect taskbar,
        int taskbarWidth,
        int taskbarHeight)
    {
        var clipped = Native.Intersect(candidate, taskbar);
        if (clipped.Right <= clipped.Left || clipped.Bottom <= clipped.Top) return;

        // Windows 11 exposes large composition/container surfaces in the UIA tree.
        // They are backgrounds, not occupied slots, so ignore them here.
        var isBackgroundSurface = clipped.Right - clipped.Left >= taskbarWidth * 0.2 &&
                                  clipped.Bottom - clipped.Top >= taskbarHeight * 0.5;
        if (!isBackgroundSurface)
        {
            occupied.Add(clipped);
        }
    }

    private static Native.Rect? FindLargestGap(Native.Rect taskbar, List<Native.Rect> occupied, bool vertical)
    {
        const int minimumGap = 240;
        var intervals = occupied
            .Select(item => vertical
                ? (Start: Math.Max(taskbar.Top, item.Top), End: Math.Min(taskbar.Bottom, item.Bottom))
                : (Start: Math.Max(taskbar.Left, item.Left), End: Math.Min(taskbar.Right, item.Right)))
            .Where(item => item.End > item.Start)
            .OrderBy(item => item.Start)
            .ToList();
        if (intervals.Count == 0) return null;

        var axisStart = vertical ? taskbar.Top : taskbar.Left;
        var axisEnd = vertical ? taskbar.Bottom : taskbar.Right;
        var cursor = axisStart;
        (int Start, int End)? best = null;
        foreach (var interval in intervals)
        {
            if (interval.Start - cursor >= minimumGap &&
                (best is null || interval.Start - cursor > best.Value.End - best.Value.Start))
            {
                best = (cursor, interval.Start);
            }

            cursor = Math.Max(cursor, interval.End);
        }

        if (axisEnd - cursor >= minimumGap &&
            (best is null || axisEnd - cursor > best.Value.End - best.Value.Start))
        {
            best = (cursor, axisEnd);
        }

        if (best is null) return null;
        return vertical
            ? new Native.Rect(taskbar.Left, best.Value.Start, taskbar.Right, best.Value.End)
            : new Native.Rect(best.Value.Start, taskbar.Top, best.Value.End, taskbar.Bottom);
    }

    private static Native.Rect? FindPreferredGap(Native.Rect taskbar, List<Native.Rect> occupied, bool vertical)
    {
        if (vertical) return FindLargestGap(taskbar, occupied, vertical);

        const int minimumGap = 240;
        var intervals = occupied
            .Select(item => (Start: Math.Max(taskbar.Left, item.Left), End: Math.Min(taskbar.Right, item.Right)))
            .Where(item => item.End > item.Start)
            .OrderBy(item => item.Start)
            .ToList();
        if (intervals.Count == 0) return null;

        var cursor = taskbar.Left;
        foreach (var interval in intervals)
        {
            if (interval.Start - cursor >= minimumGap)
            {
                return new Native.Rect(cursor, taskbar.Top, interval.Start, taskbar.Bottom);
            }

            cursor = Math.Max(cursor, interval.End);
        }

        return taskbar.Right - cursor >= minimumGap
            ? new Native.Rect(cursor, taskbar.Top, taskbar.Right, taskbar.Bottom)
            : null;
    }
}

internal sealed class TaskbarOverlayWindow : Window
{
    private readonly TextBlock _text;

    public bool IsClosed { get; private set; }
    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    public TaskbarOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        IsHitTestVisible = false;
        Focusable = false;
        _text = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 3,
                ShadowDepth = 0,
                Opacity = 0.9,
                Color = System.Windows.Media.Colors.Black
            }
        };
        Content = _text;
        Closed += (_, _) => IsClosed = true;
    }

    public void Update(string current, string? next, AppSettings settings, TaskbarLayout layout)
    {
        if (IsClosed) throw new InvalidOperationException("Taskbar overlay has been closed.");

        _text.Text = next is null ? current : $"{current}\n{next}";
        _text.FontFamily = new System.Windows.Media.FontFamily(settings.FontFamily);
        _text.FontSize = settings.FontSize * layout.Dpi / 96d;
        _text.FontWeight = settings.FontWeight();
        _text.TextAlignment = settings.Alignment.ToLowerInvariant() switch
        {
            "left" => TextAlignment.Left,
            "right" => TextAlignment.Right,
            _ => TextAlignment.Center
        };
        var color = System.Windows.Media.ColorConverter.ConvertFromString(settings.Color) is System.Windows.Media.Color parsed
            ? parsed
            : System.Windows.Media.Colors.White;
        _text.Foreground = new System.Windows.Media.SolidColorBrush(color);
        _text.Opacity = 1;

        if (!IsVisible)
        {
            Show();
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (!Native.IsWindow(layout.Handle))
        {
            throw new Win32Exception("Windows taskbar handle is no longer valid.");
        }

        var exStyle = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(handle, Native.GWL_EXSTYLE,
            new IntPtr(exStyle | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TRANSPARENT));

        var horizontal = !layout.Vertical;
        if (horizontal)
        {
            SetPosition(handle, layout);
        }
        else
        {
            SetPosition(handle, layout);
            _text.LayoutTransform = new RotateTransform(layout.ReverseVertical ? -90 : 90);
        }

        if (horizontal)
        {
            _text.LayoutTransform = null;
        }
    }

    private static void SetPosition(IntPtr handle, TaskbarLayout layout)
    {
        if (!Native.GetWindowRect(layout.Handle, out var taskbarRect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read taskbar position.");
        }

        var screenX = taskbarRect.Left + layout.ContentX;
        var screenY = taskbarRect.Top + layout.ContentY;
        if (!Native.SetWindowPos(handle, Native.HWND_TOPMOST, screenX, screenY, layout.ContentWidth, layout.ContentHeight,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to position taskbar overlay.");
        }
    }

    public void HideOverlay()
    {
        if (!IsClosed && IsVisible) Hide();
    }
}

internal static class AppSettingsPresentation
{
    public static FontWeight FontWeight(this AppSettings settings) => settings.FontWeightValue switch
    {
        >= 700 => FontWeights.Bold,
        >= 600 => FontWeights.SemiBold,
        _ => FontWeights.Normal
    };
}

internal static class Native
{
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const long WS_CHILD = 0x40000000;
    public const long WS_VISIBLE = 0x10000000;
    public const long WS_POPUP = unchecked((long)0x80000000);
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_TRANSPARENT = 0x00000020;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public delegate bool EnumWindowsProc(IntPtr handle, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll")]
    public static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr handle);

    public static Rect Intersect(Rect first, Rect second) => new(
        Math.Max(first.Left, second.Left),
        Math.Max(first.Top, second.Top),
        Math.Min(first.Right, second.Right),
        Math.Min(first.Bottom, second.Bottom));

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public Rect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }
}
