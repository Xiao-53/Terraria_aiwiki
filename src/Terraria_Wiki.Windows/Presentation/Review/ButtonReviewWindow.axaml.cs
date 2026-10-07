using System.Windows.Input;
using Terraria_Wiki.Windows.Presentation.Infrastructure;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Terraria_Wiki.Windows.Presentation.Review;

// Isolated UI samples only: no product services or real view models are constructed.
public sealed partial class ButtonReviewWindow : Window
{
    private StackPanel _samples = null!;
    private TextBlock _feedback = null!;
    private ReviewCommand _sendCommand = null!;
    private bool _answering;
    private bool _favorite;
    private Button _send = null!, _stop = null!, _newChat = null!, _favoriteButton = null!;
    private StackPanel _downloads = null!;
    private bool _confirmationOpen;

    public ButtonReviewWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _samples = this.FindControl<StackPanel>("Samples")!;
        _feedback = this.FindControl<TextBlock>("Feedback")!;
        RequestedThemeVariant = ThemeVariant.Light;
        BuildSamples();
        Classes.Set("interaction-feedback", true);
    }

    private void Tell(string text) => _feedback.Text = "模拟反馈：" + text;
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
    private Button Action(string text, string classes, Action? action = null)
    {
        var button = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center };
        foreach (var name in classes.Split(' ')) button.Classes.Add(name);
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => { if (action is null) Tell(text); else action(); };
        return button;
    }
    private static WrapPanel Row(params Control[] controls)
    {
        var row = new WrapPanel();
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 0, 8, 8);
            row.Children.Add(control);
        }
        return row;
    }
    private void Section(string title, params Control[] controls)
    {
        var section = new StackPanel { Spacing = 8 };
        var heading = Text(title); heading.Classes.Add("section-title");
        section.Children.Add(heading);
        foreach (var control in controls) section.Children.Add(control);
        _samples.Children.Add(section);
    }
    private Control CreateIcon(Button owner, string key, bool filled = false, int size = 20)
    {
        // Preserve the 24-DIP design canvas; fitting asymmetric bounds into a square
        // leaves spare space on one axis and can make the visible glyph look off-center.
        if (key is "IconSend" or "IconDelete")
        {
            var glyph = new ShapePath
            {
                Data = (Geometry)this.FindResource(key)!, Stretch = Stretch.None,
                StrokeThickness = key == "IconSend" ? 2.2 : 1.8,
                StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round
            };
            glyph.Bind(ShapePath.StrokeProperty, new Binding(nameof(Button.Foreground)) { Source = owner });
            return new Grid { Width = 24, Height = 24, Children = { glyph } };
        }
        var path = new ShapePath
        {
            Data = (Geometry)this.FindResource(key)!, Width = size, Height = size,
            Stretch = Stretch.Uniform, StrokeThickness = filled ? 0 : 1.8,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round
        };
        path.Bind(filled ? ShapePath.FillProperty : ShapePath.StrokeProperty,
            new Binding(nameof(Button.Foreground)) { Source = owner });
        return path;
    }
    private Button IconAction(string name, string key, string classes = "icon-button", Action? action = null, bool filled = false)
    {
        var button = Action(name, classes, action);
        button.Content = CreateIcon(button, key, filled);
        ToolTip.SetTip(button, name);
        return button;
    }
    private Button Toolbar(string label, string key, Action? action = null)
    {
        var button = Action(label, "toolbar-action", action);
        button.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { CreateIcon(button, key, size: 16), Text(label) } };
        return button;
    }
    private void BuildSamples()
    {
        _samples.Children.Clear();
        Section("BTN-01 / 02 / 03 · 数据管理：普通操作与确认（36 DIP 起）",
            Row(Action("导出数据库", "primary-action"), Action("导入数据库", "secondary-action"),
                Action("清理失败记录", "danger-action", RequestConfirmation)));
        var refresh = Toolbar("刷新", "IconRefresh");
        var reload = Action("重新载入", "toolbar-action");
        var save = Action("保存并应用", "primary-action"); save.Height = 40;
        Section("BTN-04 / 01 · 收藏、资料库、管理刷新；设置页头（40 DIP）", Row(refresh, reload, save));
        _favoriteButton = IconAction("收藏", "IconFavorite", action: ToggleFavorite);
        Section("BTN-05 / 06 / 07 · Shell、Reader、收藏、历史操作（44 DIP）",
            Row(IconAction("主页", "IconHome", filled: true), IconAction("返回搜索", "IconBack", filled: true),
                IconAction("页面后退", "IconPreviousPage", filled: true), IconAction("搜索", "IconSearch", filled: true),
                _favoriteButton, IconAction("关闭 Reader", "IconClose"),
                IconAction("请求删除历史对话", "IconDelete", "icon-button danger-icon", RequestConfirmation)));
        var pages = Action("全部页面", "segment-button"); var history = Action("对话历史", "segment-button");
        pages.Classes.Add("selected");
        pages.Click += (_, _) => { pages.Classes.Add("selected"); history.Classes.Remove("selected"); };
        history.Click += (_, _) => { history.Classes.Add("selected"); pages.Classes.Remove("selected"); };
        var general = Action("常规设置", "segment-button"); general.Width = 135; general.Classes.Add("selected");
        var ai = Action("AI 配置", "segment-button"); ai.Width = 135;
        general.Click += (_, _) => { general.Classes.Add("selected"); ai.Classes.Remove("selected"); };
        ai.Click += (_, _) => { ai.Classes.Add("selected"); general.Classes.Remove("selected"); };
        Section("BTN-08 · 资料库分段；设置二级菜单（135 DIP 宽）", Row(pages, history), Row(general, ai));
        var navRow = new WrapPanel(); var navButtons = new List<Button>();
        foreach (var (name, key) in new[] { ("AI 问答", "IconChat"), ("Wiki", "IconSearch"), ("收藏", "IconFavorite"),
            ("资料库", "IconLibrary"), ("数据", "IconData"), ("设置", "IconSettings") })
        {
            var b = Action(name, "rail-nav"); ToolTip.SetTip(b, name);
            var indicator = new Border { Classes = { "rail-indicator" }, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = navButtons.Count == 0 };
            var label = Text(name); label.FontSize = 10; label.HorizontalAlignment = HorizontalAlignment.Center;
            b.Content = new Grid { Children = { indicator, new StackPanel { VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, Spacing = 3, Children = { CreateIcon(b, key, true), label } } } };
            if (navButtons.Count == 0) b.Classes.Add("selected");
            b.Click += (_, _) => {
                foreach (var item in navButtons) { item.Classes.Remove("selected"); ((Grid)item.Content!).Children[0].IsVisible = false; }
                b.Classes.Add("selected"); indicator.IsVisible = true;
            };
            navButtons.Add(b); navRow.Children.Add(b);
        }
        Section("BTN-09 · Shell 导航（56 × 56 DIP，含选中指示条）", navRow);
        var list = Action("打开 Wiki 页面", "list-row");
        list.HorizontalAlignment = HorizontalAlignment.Stretch;
        list.Content = new StackPanel { Spacing = 3, Children = { Text("天顶剑 · 长标题与自然换行审核：收藏、资料库和搜索结果"),
            new TextBlock { Text = "页面详情与时间 · 点击整行查看模拟反馈", Classes = { "muted" } } } };
        var source = Action("打开可信 Wiki 来源", "source-row"); source.HorizontalAlignment = HorizontalAlignment.Stretch;
        var sourceGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        sourceGrid.Children.Add(new StackPanel { Children = { Text("天顶剑"), new TextBlock { Text = "可信来源 · 合成与获取", Classes = { "muted" } } } });
        var arrow = Text("›"); Grid.SetColumn(arrow, 1); sourceGrid.Children.Add(arrow); source.Content = sourceGrid;
        Section("BTN-10 / 11 · 列表行、可信来源（随内容撑高）", list, source);
        Section("BTN-12 · Shell 预览入口", Row(Action("Wiki 预览", "wiki-preview-button")));
        _sendCommand = new ReviewCommand(() => SetAnswering(true), () => !_answering && this.FindControl<CheckBox>("AllowSend")!.IsChecked == true);
        _send = IconAction("发送问题", "IconSend", "send-button", () => { }, true); _send.Command = _sendCommand;
        _stop = Action("停止回答", "stop-button", () => SetAnswering(false)); ToolTip.SetTip(_stop, "停止回答");
        var square = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(2) };
        square.Bind(Border.BackgroundProperty, new Binding(nameof(Button.Foreground)) { Source = _stop }); _stop.Content = square;
        _newChat = Toolbar("新对话", "IconAdd", () => { _favorite = false; UpdateFavorite(); Tell("已开始空白新对话"); }); _newChat.MinHeight = 44;
        Section("BTN-04 / 13 / 14 · Chat 新对话与发送／停止（44 DIP）", Row(_newChat, _send, _stop));
        Section("BTN-02 / 03 · 资料库／数据管理模态确认", Row(Action("请求删除", "danger-action", RequestConfirmation)));
        _downloads = new StackPanel { Spacing = 12 };
        Section("MG-09 / 10 / 11 · 下载层级模拟", Row(Action("空库", "segment-button", () => SetDownloads(0)),
            Action("已有轻量库", "segment-button", () => SetDownloads(1)), Action("已有全量库", "segment-button", () => SetDownloads(2))), _downloads);
        var maintenance = new WrapPanel { ItemWidth = 170, ItemHeight = 44 };
        foreach (var (label, style) in new[] { ("重试失败项", "secondary-action"), ("清理失败记录", "danger-action"),
            ("整理数据库", "secondary-action"), ("删除全部资源", "danger-action"), ("删除当前 Wiki 数据", "danger-action") })
        { var b = Action(label, style); b.Margin = new Thickness(0, 0, 8, 8); maintenance.Children.Add(b); }
        Section("BTN-02 / 03 · 数据管理维护区（170 × 44 DIP 单元，含边距）", maintenance);
        SetAnswering(false); SetDownloads(0); UpdateFavorite();
    }
    private async void RequestConfirmation()
    {
        if (_confirmationOpen) return;
        _confirmationOpen = true;
        try
        {
            var dialog = new ConfirmationOverlay(new UiTextCatalog());
            dialog.Update("删除演示记录", "是否删除这条演示记录？", "确认删除", true, true);
            Tell("等待模拟确认");
            var approved = await dialog.ShowAsync(this);
            Tell(approved ? "演示记录已删除；可重置" : "已取消，记录保留");
        }
        finally { _confirmationOpen = false; }
    }
    private void ToggleFavorite() { _favorite = !_favorite; UpdateFavorite(); Tell(_favorite ? "已收藏" : "已取消收藏"); }
    private void UpdateFavorite()
    {
        var path = (ShapePath)CreateIcon(_favoriteButton, "IconFavorite");
        if (_favorite)
        {
            path.Bind(ShapePath.FillProperty, new DynamicResourceExtension("TerrariaFavoriteBrush"));
            path.Bind(ShapePath.StrokeProperty, new DynamicResourceExtension("TerrariaFavoriteBrush"));
        }
        _favoriteButton.Content = path;
        var name = _favorite ? "取消收藏" : "收藏";
        AutomationProperties.SetName(_favoriteButton, name); ToolTip.SetTip(_favoriteButton, name);
    }
    private void SetAnswering(bool value)
    {
        _answering = value; _send.IsVisible = !value; _stop.IsVisible = value;
        _newChat.IsEnabled = !value; _sendCommand.Refresh(); Tell(value ? "正在模拟回答；新对话暂不可用" : "已停止／可以发送");
    }
    private void SetDownloads(int state)
    {
        _downloads.Children.Clear();
        void Add(string label, string detail, string style)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 16 };
            var b = Action(label, style); b.HorizontalAlignment = HorizontalAlignment.Left; row.Children.Add(b);
            var text = Text(detail); text.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(text, 1); row.Children.Add(text);
            _downloads.Children.Add(row);
        }
        if (state != 0) Add("检查更新", "自动检查 Wiki 并更新", "primary-action");
        if (state != 2) Add("全量下载", "约 4 GB，包含页面、图片、音频等全部资源", state == 0 ? "primary-action" : "secondary-action");
        if (state != 1) Add("轻量下载", "约 800 MB，只包含基础页面，没有图片和音频", state == 0 ? "primary-action" : "secondary-action");
        Tell(new[] { "空库：两个下载入口均为主按钮", "轻量库：更新为主，全量下载为副", "全量库：更新为主，轻量下载为副" }[state]);
    }
    private void OnFeedbackChanged(object? sender, RoutedEventArgs e)
    {
        var enabled = this.FindControl<CheckBox>("EnableFeedback")?.IsChecked == true;
        Classes.Set("interaction-feedback", enabled);
        if (_feedback is not null)
            Tell(enabled ? "交互动效已开启（独立于 Windows 设置）" : "交互动效已关闭，状态立即切换");
    }
    private void OnLight(object? sender, RoutedEventArgs e) => RequestedThemeVariant = ThemeVariant.Light;
    private void OnDark(object? sender, RoutedEventArgs e) => RequestedThemeVariant = ThemeVariant.Dark;
    private void OnMinimum(object? sender, RoutedEventArgs e) { WindowState = WindowState.Normal; Width = 900; Height = 600; }
    private void OnDefault(object? sender, RoutedEventArgs e) { WindowState = WindowState.Normal; Width = 1280; Height = 820; }
    private void OnReset(object? sender, RoutedEventArgs e)
    {
        this.FindControl<CheckBox>("DisableSamples")!.IsChecked = false;
        this.FindControl<CheckBox>("AllowSend")!.IsChecked = true;
        _favorite = false; _answering = false; BuildSamples(); Tell("所有模拟已重置");
    }
    private void OnDisabledChanged(object? sender, RoutedEventArgs e)
    { if (_samples is not null) _samples.IsEnabled = this.FindControl<CheckBox>("DisableSamples")!.IsChecked != true; }
    private void OnSendAllowedChanged(object? sender, RoutedEventArgs e) => _sendCommand?.Refresh();

    private sealed class ReviewCommand(Action execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute();
        public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
