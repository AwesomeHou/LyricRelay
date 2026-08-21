using System.Windows;
using System.IO;

namespace LyricRelay.Windows;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        UpdateSettingLabels();
    }

    public event EventHandler? SettingsChanged;
    public event EventHandler? PairingRefreshRequested;
    public event EventHandler? ConnectionRefreshRequested;

    public bool IsLyricsEnabled => ShowLyrics.IsChecked == true;
    public bool IsStartWithWindowsEnabled => StartWithWindows.IsChecked == true;
    public bool IsAutoConnectEnabled => AutoConnect.IsChecked == true;
    public double SelectedFontSize => FontSizeSlider.Value;
    public int SelectedOffsetMs => (int)Math.Round(OffsetSlider.Value);
    public string SelectedAlignment => (AlignmentBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Center";
    public int SelectedFontWeight => int.TryParse((WeightBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString(), out var weight) ? weight : 600;
    public string SelectedColor => ColorBox.Text;
    public string SelectedFontFamily => FontFamilyBox.Text;

    public void SetPairing(string text, byte[] png)
    {
        PairingText.Text = text;
        using var stream = new MemoryStream(png);
        var image = new System.Windows.Media.Imaging.BitmapImage();
        image.BeginInit();
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        PairingQr.Source = image;
    }

    public void SetConnection(string text) => ConnectionText.Text = $"连接：{text}";
    public void SetLyrics(string text) => LyricsText.Text = $"歌词来源：{text}";

    public void ApplySettings(AppSettings settings)
    {
        ShowLyrics.IsChecked = settings.ShowLyrics;
        StartWithWindows.IsChecked = settings.StartWithWindows;
        AutoConnect.IsChecked = settings.AutoConnect;
        FontSizeSlider.Value = settings.FontSize;
        OffsetSlider.Value = settings.OffsetMs;
        AlignmentBox.SelectedItem = AlignmentBox.Items
            .OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Content?.ToString(), settings.Alignment, StringComparison.OrdinalIgnoreCase));
        WeightBox.SelectedItem = WeightBox.Items
            .OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(item => item.Content?.ToString() == settings.FontWeightValue.ToString());
        ColorBox.Text = settings.Color;
        FontFamilyBox.Text = settings.FontFamily;
        UpdateSettingLabels();
    }

    private void OnSettingsChanged(object sender, RoutedEventArgs e)
    {
        UpdateSettingLabels();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSettingLabels();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnRefreshPairing(object sender, RoutedEventArgs e)
    {
        PairingRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRefreshConnection(object sender, RoutedEventArgs e)
    {
        ConnectionRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSettingLabels()
    {
        if (FontSizeValue is null || OffsetValue is null || FontSizeSlider is null || OffsetSlider is null)
        {
            return;
        }

        FontSizeValue.Text = $"{FontSizeSlider.Value:0}";
        OffsetValue.Text = $"{OffsetSlider.Value:0}";
    }
}
