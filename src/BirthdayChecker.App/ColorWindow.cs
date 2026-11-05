using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BirthdayChecker.Core;

namespace BirthdayChecker.App;

/// <summary>Color picker in the look of the Windows 11 one: the system dialog on Windows 10 is the old one.</summary>
internal sealed class ColorWindow : Window
{
    private const int Area = 256, MaxCustom = 24;

    /// <summary>The 48 basic colors of the Windows dialog, as its 12 × 4 grid shows them.</summary>
    private static readonly string[] Basic =
    [
        "#FF8080", "#FF0000", "#804040", "#800000", "#400000", "#80FFFF", "#00FFFF", "#0080C0", "#0000FF", "#0000A0", "#004080", "#000080",
        "#FFFF80", "#FFFF00", "#FF8040", "#FF8000", "#804000", "#808000", "#8080FF", "#8000FF", "#0080FF", "#000040", "#800080", "#400040",
        "#80FF80", "#80FF00", "#00FF00", "#00FF40", "#00FF80", "#808040", "#FF80C0", "#FF80FF", "#FF00FF", "#FF0080", "#8080C0", "#800040",
        "#008000", "#008040", "#008080", "#408080", "#004000", "#004040", "#400080", "#000000", "#808080", "#C0C0C0", "#E0E0E0", "#FFFFFF",
    ];

    private static readonly Lazy<BitmapSource> Spectrum = new(MakeSpectrum);

    private readonly Canvas _spectrumMarks = new() { IsHitTestVisible = false };
    private readonly Ellipse _spectrumRing = Ring();
    private readonly Ellipse _valueRing = Ring();
    private readonly Border _preview = new() { Width = 44, Height = Area, CornerRadius = new CornerRadius(4), Margin = new Thickness(12, 0, 12, 0) };
    private readonly Border _valueBar = new() { Width = 14, Height = Area, CornerRadius = new CornerRadius(7), Cursor = Cursors.Hand };
    private readonly TextBox _hex = new() { Width = 130, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox _mode = new() { Width = 130, ItemsSource = new[] { "RGB", "HSV" }, SelectedIndex = 0, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox[] _fields = new TextBox[3];
    private readonly TextBlock[] _labels = new TextBlock[3];
    private readonly UniformGrid _custom = new() { Columns = 6, Rows = 4 };
    private Hsv _color;
    private bool _updating;

    public string Result => ColorMath.ToHex(_color);

    public ColorWindow(string? current)
    {
        ColorMath.TryParseHex(current, out _color); // black when there is none, as in the Windows dialog
        Title = "Выбор цвета";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(SpectrumArea());
        top.Children.Add(_preview);
        top.Children.Add(ValueArea());
        top.Children.Add(Numbers());

        var grids = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        grids.Children.Add(Titled("Базовые цвета", Dots(Basic, 12, 4), new Thickness(0, 0, 28, 0)));
        grids.Children.Add(CustomArea());

        var ready = new WrapPanel();
        foreach (var hex in ColorPick.PaletteColors)
            ready.Children.Add(Dot(hex));

        var ok = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(0, 0, 6, 0) }.Styled("MainButton");
        ok.Click += (_, _) => DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Отмена", IsCancel = true }.Styled("MainButton"));

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(top);
        root.Children.Add(grids);
        root.Children.Add(Titled("Готовые цвета", ready, new Thickness(0, 12, 0, 0)));
        root.Children.Add(buttons);
        Content = root;
        FillCustom();
        ShowColor();
    }

    private static Ellipse Ring() => new()
    {
        Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false,
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 2, ShadowDepth = 0 }, // seen on white too
    };

    private static BitmapSource MakeSpectrum()
    {
        var pixels = new byte[Area * Area * 4];
        for (int y = 0; y < Area; y++)
            for (int x = 0; x < Area; x++)
            {
                var (r, g, b) = ColorMath.ToRgb(new Hsv(x * 360d / Area, 1 - y / (Area - 1d), 1));
                int i = (y * Area + x) * 4;
                (pixels[i], pixels[i + 1], pixels[i + 2]) = (b, g, r);
            }
        var bitmap = BitmapSource.Create(Area, Area, 96, 96, PixelFormats.Bgr32, null, pixels, Area * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private Grid SpectrumArea()
    {
        var image = new Image { Source = Spectrum.Value, Width = Area, Height = Area, Stretch = Stretch.Fill, Cursor = Cursors.Cross };
        image.Clip = new RectangleGeometry(new Rect(0, 0, Area, Area), 4, 4);
        Draggable(image, p => Set(_color with
        {
            H = Math.Clamp(p.X, 0, Area - 1) * 360 / Area,
            S = 1 - Math.Clamp(p.Y, 0, Area - 1) / (Area - 1),
            V = _color.V == 0 ? 1 : _color.V, // from black, a click on a color must show that color
        }));
        _spectrumMarks.Children.Add(_spectrumRing);
        var area = new Grid { Width = Area, Height = Area };
        area.Children.Add(image);
        area.Children.Add(_spectrumMarks);
        return area;
    }

    private Grid ValueArea()
    {
        Draggable(_valueBar, p => Set(_color with { V = 1 - Math.Clamp(p.Y, 0, Area - 1) / (Area - 1) }));
        var marks = new Canvas { IsHitTestVisible = false };
        marks.Children.Add(_valueRing);
        var area = new Grid { Width = 14, Margin = new Thickness(0, 0, 16, 0) };
        area.Children.Add(_valueBar);
        area.Children.Add(marks);
        return area;
    }

    private static void Draggable(FrameworkElement element, Action<Point> at)
    {
        element.MouseLeftButtonDown += (_, e) =>
        {
            element.CaptureMouse();
            at(e.GetPosition(element));
        };
        element.MouseMove += (_, e) =>
        {
            if (element.IsMouseCaptured)
                at(e.GetPosition(element));
        };
        element.MouseLeftButtonUp += (_, _) => element.ReleaseMouseCapture();
    }

    private StackPanel Numbers()
    {
        var panel = new StackPanel();
        _hex.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(_hex);
        panel.Children.Add(_mode);
        _hex.TextChanged += (_, _) =>
        {
            if (!_updating && ColorMath.TryParseHex(_hex.Text, out var color))
                Set(color, typing: _hex);
        };
        _mode.SelectionChanged += (_, _) => ShowColor();
        for (int i = 0; i < 3; i++)
        {
            var box = _fields[i] = new TextBox { Width = 90, VerticalContentAlignment = VerticalAlignment.Center };
            box.TextChanged += (_, _) => OnField(box);
            _labels[i] = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            row.Children.Add(box);
            row.Children.Add(_labels[i]);
            panel.Children.Add(row);
        }
        return panel;
    }

    private bool IsHsvMode => _mode.SelectedIndex == 1;

    private void OnField(TextBox typing)
    {
        if (_updating)
            return;
        var values = new int[3];
        for (int i = 0; i < 3; i++)
            if (!int.TryParse(_fields[i].Text, NumberStyles.None, CultureInfo.InvariantCulture, out values[i]))
                return; // half-typed: wait
        if (IsHsvMode)
            Set(new Hsv(Math.Min(values[0], 360), Math.Min(values[1], 100) / 100d, Math.Min(values[2], 100) / 100d), typing);
        else
            Set(ColorMath.ToHsv((byte)Math.Min(values[0], 255), (byte)Math.Min(values[1], 255), (byte)Math.Min(values[2], 255)), typing);
    }

    /// <summary>A gray has no hue and black no saturation: what the user set stays for when they come back.</summary>
    private void Set(Hsv color, TextBox? typing = null)
    {
        _color = color with
        {
            H = color.S == 0 || color.V == 0 ? _color.H : color.H,
            S = color.V == 0 ? _color.S : color.S,
        };
        ShowColor(typing);
    }

    /// <param name="typing">The field being typed in keeps its text as is.</param>
    private void ShowColor(TextBox? typing = null)
    {
        _updating = true;
        var (r, g, b) = ColorMath.ToRgb(_color);
        _preview.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        var (fr, fg, fb) = ColorMath.ToRgb(_color with { V = 1 });
        _valueBar.Background = new LinearGradientBrush(Color.FromRgb(fr, fg, fb), Colors.Black, 90);
        Canvas.SetLeft(_spectrumRing, _color.H % 360 / 360 * Area - 6);
        Canvas.SetTop(_spectrumRing, (1 - _color.S) * (Area - 1) - 6);
        Canvas.SetLeft(_valueRing, 1);
        Canvas.SetTop(_valueRing, (1 - _color.V) * (Area - 1) - 6);
        if (!ReferenceEquals(typing, _hex))
            _hex.Text = ColorMath.ToHex(_color);
        string[] names = IsHsvMode ? ["Оттенок", "Насыщенность", "Яркость"] : ["Красный", "Зелёный", "Синий"];
        int[] values = IsHsvMode
            ? [(int)Math.Round(_color.H), (int)Math.Round(_color.S * 100), (int)Math.Round(_color.V * 100)]
            : [r, g, b];
        for (int i = 0; i < 3; i++)
        {
            _labels[i].Text = names[i];
            if (!ReferenceEquals(typing, _fields[i]))
                _fields[i].Text = values[i].ToString(CultureInfo.InvariantCulture);
        }
        _updating = false;
    }

    private static StackPanel Titled(string title, UIElement content, Thickness margin)
    {
        var panel = new StackPanel { Margin = margin };
        panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(content);
        return panel;
    }

    private UniformGrid Dots(IEnumerable<string> colors, int columns, int rows)
    {
        var grid = new UniformGrid { Columns = columns, Rows = rows };
        foreach (var hex in colors)
            grid.Children.Add(Dot(hex));
        return grid;
    }

    private Ellipse Dot(string hex)
    {
        var dot = new Ellipse
        {
            Width = 20, Height = 20, Margin = new Thickness(2), Fill = ColorPick.Brush(hex), Cursor = Cursors.Hand, ToolTip = hex,
            StrokeThickness = 1,
        };
        dot.SetResourceReference(Shape.StrokeProperty, "GridLine"); // white and black must stay visible on their theme
        dot.MouseLeftButtonUp += (_, _) =>
        {
            if (ColorMath.TryParseHex(hex, out var color))
                Set(color);
        };
        return dot;
    }

    private StackPanel CustomArea()
    {
        var add = new Button { Content = "+", Width = 24, Height = 24, MinHeight = 0, Padding = new Thickness(0), ToolTip = "Добавить текущий цвет" };
        add.Click += (_, _) => AddCustom();
        var title = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(add, Dock.Right);
        title.Children.Add(add);
        title.Children.Add(new TextBlock { Text = "Свои цвета", VerticalAlignment = VerticalAlignment.Center });
        var panel = new StackPanel();
        panel.Children.Add(title);
        panel.Children.Add(_custom);
        return panel;
    }

    private void FillCustom()
    {
        var colors = ColorPick.LoadCustom();
        _custom.Children.Clear();
        foreach (var hex in colors)
            _custom.Children.Add(Dot(hex));
        for (int i = colors.Count; i < MaxCustom; i++)
        {
            var empty = new Ellipse { Width = 20, Height = 20, Margin = new Thickness(2), StrokeThickness = 1, StrokeDashArray = [2, 2] };
            empty.SetResourceReference(Shape.StrokeProperty, "GridLine");
            _custom.Children.Add(empty);
        }
    }

    /// <summary>A color already there moves to the end; past the limit the oldest one goes.</summary>
    private void AddCustom()
    {
        var hex = Result;
        var colors = ColorPick.LoadCustom().Where(c => !c.Equals(hex, StringComparison.OrdinalIgnoreCase)).Append(hex).ToList();
        ColorPick.SaveCustom(colors.Skip(Math.Max(0, colors.Count - MaxCustom)).ToList());
        FillCustom();
    }
}
