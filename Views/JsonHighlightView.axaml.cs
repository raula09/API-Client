using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ApiClient.Views;

public partial class JsonHighlightView : UserControl
{
    private static readonly IBrush BrushDefault    = new SolidColorBrush(Color.Parse("#D4D4D4"));
    private static readonly IBrush BrushKey        = new SolidColorBrush(Color.Parse("#9CDCFE")); 
    private static readonly IBrush BrushString     = new SolidColorBrush(Color.Parse("#CE9178")); 
    private static readonly IBrush BrushNumber     = new SolidColorBrush(Color.Parse("#B5CEA8")); 
    private static readonly IBrush BrushBool       = new SolidColorBrush(Color.Parse("#569CD6")); 
    private static readonly IBrush BrushNull       = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush BrushPunct      = new SolidColorBrush(Color.Parse("#D4D4D4")); 
    private static readonly IBrush BrushLineNumber = new SolidColorBrush(Color.Parse("#858585")); 

    public static readonly StyledProperty<string?> JsonTextProperty =
        AvaloniaProperty.Register<JsonHighlightView, string?>(nameof(JsonText));

    public string? JsonText
    {
        get => GetValue(JsonTextProperty);
        set => SetValue(JsonTextProperty, value);
    }

    static JsonHighlightView()
    {
        JsonTextProperty.Changed.AddClassHandler<JsonHighlightView>((ctrl, _) => ctrl.Rebuild());
    }

    public JsonHighlightView()
    {
        InitializeComponent();
    }

    private void Rebuild()
    {
        var linesControl = this.FindControl<ItemsControl>("LinesControl")!;
        linesControl.ItemsSource = null;

        var text = JsonText;
        if (string.IsNullOrWhiteSpace(text))
            return;

        text = TryPrettyPrint(text);

        var lineWidgets = new List<Control>();
        var rawLines    = text.Split('\n');

        for (var i = 0; i < rawLines.Length; i++)
        {
            var row = BuildLineRow(i + 1, rawLines[i].TrimEnd('\r'));
            lineWidgets.Add(row);
        }

        linesControl.ItemsSource = lineWidgets;
    }

    private static Control BuildLineRow(int lineNumber, string lineText)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };

        panel.Children.Add(new TextBlock
        {
            Text              = lineNumber.ToString().PadLeft(4),
            Foreground        = BrushLineNumber,
            FontFamily        = new FontFamily("Cascadia Code,Consolas,monospace"),
            FontSize          = 13,
            Margin            = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Top,
            MinWidth          = 32
        });

        var contentPanel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (text, brush) in TokeniseLine(lineText))
        {
            contentPanel.Children.Add(new TextBlock
            {
                Text       = text,
                Foreground = brush,
                FontFamily = new FontFamily("Cascadia Code,Consolas,monospace"),
                FontSize   = 13
            });
        }

        panel.Children.Add(contentPanel);
        return panel;
    }

    // ── simple line-level tokeniser ───────────────────────────────────────────
    // Recognises: indent whitespace · object keys · string values · numbers ·
    //             booleans · null · punctuation ( { } [ ] , : )
    private static IEnumerable<(string text, IBrush brush)> TokeniseLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            yield return (" ", BrushDefault);
            yield break;
        }

        var pos = 0;

        var wsEnd = 0;
        while (wsEnd < line.Length && line[wsEnd] == ' ') wsEnd++;
        if (wsEnd > 0)
        {
            yield return (line[..wsEnd], BrushDefault);
            pos = wsEnd;
        }

        while (pos < line.Length)
        {
            var ch = line[pos];

            if (ch == '"')
            {
                var (strToken, len) = ReadQuotedString(line, pos);

                var afterStr = pos + len;
                while (afterStr < line.Length && line[afterStr] == ' ') afterStr++;
                var isKey = afterStr < line.Length && line[afterStr] == ':';

                yield return (strToken, isKey ? BrushKey : BrushString);
                pos += len;
                continue;
            }

            if (ch is '{' or '}' or '[' or ']' or ',' or ':')
            {
                yield return (ch.ToString(), BrushPunct);
                pos++;
       
                if (ch == ':' && pos < line.Length && line[pos] == ' ')
                {
                    yield return (" ", BrushDefault);
                    pos++;
                }
                continue;
            }

            if (char.IsDigit(ch) || (ch == '-' && pos + 1 < line.Length && char.IsDigit(line[pos + 1])))
            {
                var numEnd = pos + (ch == '-' ? 1 : 0);
                while (numEnd < line.Length && (char.IsDigit(line[numEnd]) || line[numEnd] == '.' || line[numEnd] == 'e' || line[numEnd] == 'E' || line[numEnd] == '+' || line[numEnd] == '-'))
                    numEnd++;
                yield return (line[pos..numEnd], BrushNumber);
                pos = numEnd;
                continue;
            }

            if (TryMatchKeyword(line, pos, "true",  out var klen) ||
                TryMatchKeyword(line, pos, "false", out klen)     ||
                TryMatchKeyword(line, pos, "null",  out klen))
            {
                var keyword = line[pos..(pos + klen)];
                yield return (keyword, keyword == "null" ? BrushNull : BrushBool);
                pos += klen;
                continue;
            }

            yield return (ch.ToString(), BrushDefault);
            pos++;
        }
    }

    private static (string token, int length) ReadQuotedString(string line, int start)
    {
        var i = start + 1;
        while (i < line.Length)
        {
            if (line[i] == '\\') { i += 2; continue; } 
            if (line[i] == '"')  { i++;     break;    }
            i++;
        }
        var token = line[start..i];
        return (token, token.Length);
    }

    private static bool TryMatchKeyword(string line, int pos, string keyword, out int length)
    {
        length = keyword.Length;
        if (pos + length > line.Length) return false;
        if (!line.AsSpan(pos, length).Equals(keyword.AsSpan(), StringComparison.Ordinal)) return false;
     
        var after = pos + length;
        if (after < line.Length && (char.IsLetterOrDigit(line[after]) || line[after] == '_')) return false;
        return true;
    }

    private static string TryPrettyPrint(string raw)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(raw);
            return System.Text.Json.JsonSerializer.Serialize(
                doc.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return raw; 
        }
    }
}
