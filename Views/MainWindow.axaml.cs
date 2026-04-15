using Avalonia.Controls;

namespace ApiClient.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        this.Opened += (s, e) => WireUpTabHandlers();
    }

    private void WireUpTabHandlers()
    {
        // Find the response TabControl and wire up the tab change event
        if (this.FindControl<TabControl>("ResponseTabs") is { } responseTabs)
        {
            responseTabs.SelectionChanged += (s, e) =>
            {
                var bodyView = this.FindControl<TextBox>("ResponseBodyView");
                var headersView = this.FindControl<ScrollViewer>("ResponseHeadersView");
                
                if (bodyView != null && headersView != null)
                {
                    bodyView.IsVisible = responseTabs.SelectedIndex == 0;
                    headersView.IsVisible = responseTabs.SelectedIndex == 1;
                }
            };
            
            // Initialize visibility
            var bodyView = this.FindControl<TextBox>("ResponseBodyView");
            var headersView = this.FindControl<ScrollViewer>("ResponseHeadersView");
            if (bodyView != null && headersView != null)
            {
                bodyView.IsVisible = true;
                headersView.IsVisible = false;
            }
        }
    }
}
