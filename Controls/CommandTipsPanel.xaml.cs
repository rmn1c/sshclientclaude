using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SshClient.Models;

namespace SshClient.Controls;

public partial class CommandTipsPanel : UserControl
{
    public static readonly DependencyProperty SendTipCommandProperty =
        DependencyProperty.Register(nameof(SendTipCommand), typeof(System.Windows.Input.ICommand),
            typeof(CommandTipsPanel));

    public System.Windows.Input.ICommand? SendTipCommand
    {
        get => (System.Windows.Input.ICommand?)GetValue(SendTipCommandProperty);
        set => SetValue(SendTipCommandProperty, value);
    }

    private List<CommandCategory> _allCategories = [];

    public CommandTipsPanel()
    {
        InitializeComponent();
        DataContext = this;
        LoadTips();
    }

    private void LoadTips()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("CommandTips.json"));

            if (resourceName is null) return;

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            _allCategories = JsonSerializer.Deserialize<List<CommandCategory>>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

            CategoriesPanel.ItemsSource = _allCategories;
        }
        catch { /* tips are non-critical */ }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(query))
        {
            CategoriesPanel.ItemsSource = _allCategories;
            return;
        }

        var filtered = _allCategories
            .Select(cat => new CommandCategory
            {
                Name = cat.Name,
                Icon = cat.Icon,
                Tips = cat.Tips.Where(t =>
                    t.Command.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList()
            })
            .Where(cat => cat.Tips.Count > 0)
            .ToList();

        CategoriesPanel.ItemsSource = filtered;
    }

    private void CopyTip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string cmd)
        {
            try { Clipboard.SetText(cmd); } catch { }
        }
    }
}
