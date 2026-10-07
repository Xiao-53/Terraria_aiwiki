using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Terraria_Wiki.Windows.Presentation.Markdown;

internal sealed partial class SafeMarkdownView : UserControl
{
    public static readonly StyledProperty<SafeMarkdownDocument> DocumentProperty =
        AvaloniaProperty.Register<SafeMarkdownView, SafeMarkdownDocument>(
            nameof(Document),
            SafeMarkdownDocument.Empty);

    private StackPanel _documentRoot = null!;

    public SafeMarkdownView()
    {
        AvaloniaXamlLoader.Load(this);
        _documentRoot = this.FindControl<StackPanel>("DocumentRoot")!;
        Render(Document);
    }

    public SafeMarkdownDocument Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty && _documentRoot is not null)
            Render(change.NewValue as SafeMarkdownDocument ?? SafeMarkdownDocument.Empty);
    }

    private void Render(SafeMarkdownDocument document)
    {
        _documentRoot.Children.Clear();
        foreach (var block in document.Blocks)
            _documentRoot.Children.Add(CreateBlock(block));
    }

    private Control CreateBlock(SafeMarkdownBlock block) => block.Kind switch
    {
        SafeMarkdownBlockKind.Heading => CreateText(
            block.Inlines,
            fontSize: block.Level switch { 1 => 28, 2 => 23, 3 => 20, _ => 17 },
            weight: FontWeight.SemiBold),
        SafeMarkdownBlockKind.Paragraph => CreateText(block.Inlines),
        SafeMarkdownBlockKind.Code => CreateCode(block),
        SafeMarkdownBlockKind.Quote => CreateQuote(block),
        SafeMarkdownBlockKind.List => CreateList(block),
        SafeMarkdownBlockKind.ListItem => CreateListItem(block, "•"),
        SafeMarkdownBlockKind.Table => CreateTable(block),
        SafeMarkdownBlockKind.ThematicBreak => new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4),
            Background = Brushes.Gray,
            Opacity = 0.35,
        },
        _ => CreateText(block.Inlines),
    };

    private static SelectableTextBlock CreateText(
        IReadOnlyList<SafeMarkdownInline> inlines,
        double fontSize = 15,
        FontWeight? weight = null)
    {
        var text = new SelectableTextBlock
        {
            FontSize = fontSize,
            FontWeight = weight ?? FontWeight.Normal,
            LineHeight = fontSize * 1.55,
            TextWrapping = TextWrapping.Wrap,
        };
        foreach (var inline in inlines)
        {
            Inline element = new Run(inline.Text);
            if (inline.IsCode)
            {
                element.FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace");
                element.FontSize = Math.Max(12, fontSize - 1);
            }
            if (inline.IsItalic)
                element = new Italic { Inlines = { element } };
            if (inline.IsBold)
                element = new Bold { Inlines = { element } };
            text.Inlines!.Add(element);
        }
        return text;
    }

    private static Control CreateCode(SafeMarkdownBlock block)
    {
        var code = new SelectableTextBlock
        {
            Text = block.Inlines.Count == 0 ? string.Empty : block.Inlines[0].Text,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        };
        code.Classes.Add("localized-code");
        var border = new Border { Child = code };
        border.Classes.Add("markdown-code");
        return border;
    }

    private Control CreateQuote(SafeMarkdownBlock block)
    {
        var content = new StackPanel { Spacing = 8 };
        foreach (var child in block.Children)
            content.Children.Add(CreateBlock(child));
        var border = new Border { Child = content };
        border.Classes.Add("markdown-quote");
        return border;
    }

    private Control CreateList(SafeMarkdownBlock block)
    {
        var panel = new StackPanel { Spacing = 6 };
        var number = 1;
        foreach (var child in block.Children)
        {
            panel.Children.Add(CreateListItem(child, block.IsOrdered ? $"{number++}." : "•"));
        }
        return panel;
    }

    private Control CreateListItem(SafeMarkdownBlock block, string marker)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*") };
        grid.Children.Add(new SelectableTextBlock
        {
            Text = marker,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0),
        });
        var body = new StackPanel { Spacing = 6 };
        foreach (var child in block.Children)
            body.Children.Add(CreateBlock(child));
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    private static Control CreateTable(SafeMarkdownBlock block)
    {
        var rows = block.Rows ?? [];
        var columns = rows.Count == 0 ? 0 : rows.Max(row => row.Count);
        var grid = new Grid();
        for (var column = 0; column < columns; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var columnIndex = 0; columnIndex < rows[rowIndex].Count; columnIndex++)
            {
                var cell = rows[rowIndex][columnIndex];
                var border = new Border
                {
                    Child = CreateText(
                        cell.Inlines,
                        fontSize: 14,
                        weight: cell.IsHeader ? FontWeight.SemiBold : FontWeight.Normal),
                };
                border.Classes.Add("markdown-cell");
                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, columnIndex);
                grid.Children.Add(border);
            }
        }
        return new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
    }
}
