using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace SpriteEditXL.UI;

/// <summary>
/// Compact numeric spinner: a narrow TextBox with two tiny stacked ▲/▼ buttons on the right.
/// ~46px wide, 22px tall. Alternative to Avalonia's bulky NumericUpDown for cramped panels.
/// Clamp to [Minimum, Maximum]. Integer only. Changes raise <see cref="ValueChanged"/>.
/// </summary>
public sealed class TinyNumericUpDown : TemplatedControl
{
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<TinyNumericUpDown, int>(nameof(Value), 1);

    public static readonly StyledProperty<int> MinimumProperty =
        AvaloniaProperty.Register<TinyNumericUpDown, int>(nameof(Minimum), 0);

    public static readonly StyledProperty<int> MaximumProperty =
        AvaloniaProperty.Register<TinyNumericUpDown, int>(nameof(Maximum), 100);

    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, Clamp(value));
    }

    public int Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public event EventHandler<int>? ValueChanged;

    private TextBox? _text;
    private bool _suppress;

    static TinyNumericUpDown()
    {
        ValueProperty.Changed.AddClassHandler<TinyNumericUpDown>((o, e) => o.OnValueChanged((int)(e.NewValue ?? 0)));
    }

    public TinyNumericUpDown()
    {
        Template = new FuncControlTemplate((host, _) =>
        {
            var self = (TinyNumericUpDown)host;
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                MinHeight = 22
            };

            var tb = new TextBox
            {
                FontSize = 10,
                Padding = new Thickness(2, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 20,
                Background = Brushes.White,
                Foreground = Brushes.Black,
                CaretBrush = Brushes.Black,
                MinHeight = 22,
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Gray
            };
            tb.Text = self.Value.ToString();
            tb.LostFocus += (_, _) => self.CommitFromText();
            tb.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { self.CommitFromText(); e.Handled = true; }
                if (e.Key == Key.Up)    { self.Value = self.Value + 1; e.Handled = true; }
                if (e.Key == Key.Down)  { self.Value = self.Value - 1; e.Handled = true; }
            };
            // The Fluent theme darkens the TextBox's inner Border background when the
            // template-part PART_BorderElement acquires focus. Force it back to white.
            void ForceWhiteBorder()
            {
                var border = tb.GetTemplateChildren().OfType<Border>()
                    .FirstOrDefault(b => b.Name == "PART_BorderElement");
                if (border != null)
                {
                    border.Background = Brushes.White;
                    border.BorderBrush = Brushes.Gray;
                }
            }
            tb.TemplateApplied += (_, _) => ForceWhiteBorder();
            tb.GotFocus      += (_, _) => ForceWhiteBorder();
            tb.LostFocus     += (_, _) => ForceWhiteBorder();
            tb.PointerEntered += (_, _) => ForceWhiteBorder();
            tb.PointerExited  += (_, _) => ForceWhiteBorder();
            self._text = tb;
            Grid.SetColumn(tb, 0);

            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = 14
            };
            var upBtn = new Button
            {
                Content = "▲",
                FontSize = 7,
                Padding = new Thickness(0),
                Width = 14,
                Height = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            var downBtn = new Button
            {
                Content = "▼",
                FontSize = 7,
                Padding = new Thickness(0),
                Width = 14,
                Height = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            upBtn.Click += (_, _) => self.Value = self.Value + 1;
            downBtn.Click += (_, _) => self.Value = self.Value - 1;
            stack.Children.Add(upBtn);
            stack.Children.Add(downBtn);
            Grid.SetColumn(stack, 1);

            grid.Children.Add(tb);
            grid.Children.Add(stack);
            return grid;
        });
    }

    private int Clamp(int v)
    {
        if (v < Minimum) return Minimum;
        if (v > Maximum) return Maximum;
        return v;
    }

    private void CommitFromText()
    {
        if (_text == null) return;
        if (int.TryParse(_text.Text, out int v))
        {
            Value = v;
        }
        else
        {
            // Restore previous text on parse failure.
            _text.Text = Value.ToString();
        }
    }

    private void OnValueChanged(int newValue)
    {
        if (_suppress) return;
        if (_text != null && _text.Text != newValue.ToString())
        {
            _suppress = true;
            _text.Text = newValue.ToString();
            _suppress = false;
        }
        ValueChanged?.Invoke(this, newValue);
    }
}
