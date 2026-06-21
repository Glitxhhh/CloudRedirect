using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CloudRedirect.Resources;
using CloudRedirect.Services;
using CloudRedirect.Windows;

namespace CloudRedirect.Pages;

public partial class ChoiceModePage : Page
{
    private string? _currentMode;

    public ChoiceModePage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try { await RefreshStateAsync(); }
            catch { }
        };
    }

    // M16: Move SteamDetector.ReadModeSetting() off the UI thread.
    // It opens settings.json synchronously, so a slow disk would
    // block Loaded long enough for the page to flash unconfigured
    // state. Resolve the mode in Task.Run, then apply visibility.
    private async Task RefreshStateAsync()
    {
        var mode = await Task.Run(() => SteamDetector.ReadModeSetting());
        ApplyMode(mode);
    }

    private void ApplyMode(string? mode)
    {
        _currentMode = mode;

        if (_currentMode != null)
        {
            CurrentModeBanner.Visibility = Visibility.Visible;

            if (_currentMode == "cloud_redirect")
            {
                CurrentModeText.Text = S.Get("Choice_CurrentMode_CloudRedirect");
                CurrentModeDescription.Text = S.Get("Choice_CurrentMode_CloudRedirect_Desc");
                STFixerCard.Visibility = Visibility.Visible;
                CloudRedirectCard.Visibility = Visibility.Collapsed;
            }
            else
            {
                CurrentModeText.Text = S.Get("Choice_CurrentMode_STFixer");
                CurrentModeDescription.Text = S.Get("Choice_CurrentMode_STFixer_Desc");
                STFixerCard.Visibility = Visibility.Collapsed;
                CloudRedirectCard.Visibility = Visibility.Visible;
            }
        }
        else
        {
            CurrentModeBanner.Visibility = Visibility.Collapsed;
            STFixerCard.Visibility = Visibility.Visible;
            CloudRedirectCard.Visibility = Visibility.Visible;
        }
    }

    private async void STFixerCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (!await TryPersistModeAsync("stfixer", cloudRedirectEnabled: false))
            return;

        var mw = Window.GetWindow(this) as MainWindow;
        mw?.ApplyMode("stfixer");
        mw?.RootNavigation.Navigate(typeof(SetupPage));
    }

    private async void CloudRedirectCard_Click(object sender, MouseButtonEventArgs e)
    {
        // One-time consent gate; skipped once accepted.
        if (!ModeService.HasAcceptedDisclaimer())
        {
            var disclaimer = new DisclaimerWindow { Owner = Window.GetWindow(this) };
            if (disclaimer.ShowDialog() != true || !disclaimer.Accepted)
                return;
            ModeService.MarkDisclaimerAccepted();
        }

        if (!await TryPersistModeAsync("cloud_redirect", cloudRedirectEnabled: true))
            return;

        var mw = Window.GetWindow(this) as MainWindow;
        mw?.ApplyMode("cloud_redirect");
        mw?.RootNavigation.Navigate(typeof(SetupPage));
    }

    // Persists both settings.json (mode) and the pin config (cloud_redirect)
    // via ModeService. Surfaces failure so a silent disk/permissions error
    // doesn't leave the UI looking like the choice was saved when it wasn't.
    private static async Task<bool> TryPersistModeAsync(string mode, bool cloudRedirectEnabled)
    {
        try
        {
            ModeService.PersistMode(mode, cloudRedirectEnabled);
            return true;
        }
        catch (Exception ex)
        {
            await Dialog.ShowErrorAsync(
                S.Get("Common_Error"),
                S.Format("Choice_FailedSaveMode", ex.Message));
            return false;
        }
    }
}
