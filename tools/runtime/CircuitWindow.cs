using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CircuitOS.Runtime;

internal sealed class CircuitWindow : Form
{
    private readonly string _url;
    private readonly WebView2 _webView;

    public CircuitWindow(string url)
    {
        _url = url;
        Text = "CircuitOS Control Core";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 700);
        Size = new Size(1500, 920);
        BackColor = Color.FromArgb(2, 16, 30);

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(2, 16, 30)
        };
        Controls.Add(_webView);
        Shown += InitializeWebViewAsync;
    }

    private async void InitializeWebViewAsync(object? sender, EventArgs eventArgs)
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CircuitOS",
                "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await _webView.EnsureCoreWebView2Async(environment);
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Navigate(_url);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                "CircuitOS could not open its application window. Ensure the Microsoft Edge WebView2 Runtime is installed.\n\n" + exception.Message,
                "CircuitOS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
    }
}
