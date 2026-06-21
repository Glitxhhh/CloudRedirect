using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CloudRedirect.Resources;
using CloudRedirect.Services;

namespace CloudRedirect.Pages;

public partial class SettingsPage : Page
{
    private const string ReleasesUrl = "https://github.com/Selectively11/CloudRedirect/releases";

    private bool _languageLoading;
    private bool _syncLoading;
    private bool _modeLoading;
    /// <summary>Current app mode ("cloud_redirect" or "stfixer"); controls
    /// which toggles are visible and how saves are scoped.</summary>
    private string? _mode;
    /// <summary>
    /// Index of the last LanguageOptions entry that was successfully
    /// persisted to settings.json. Used to roll back the combo if a
    /// later selection-change save fails so the UI never shows a
    /// language different from what's actually on disk.
    /// </summary>
    private int _lastSavedLanguageIndex;
    /// <summary>
    /// Language options: display key -> culture code (or "system").
    /// </summary>
    private static readonly (string ResourceKey, string Code)[] LanguageOptions =
    [
        ("Settings_SystemDefault", "system"),
        ("Settings_LanguageEnglish", "en"),
        ("Settings_LanguageSpanish", "es"),
        ("Settings_LanguagePortuguese", "pt-BR"),
    ];

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            LoadAbout();
            try { await LoadSettingsAsync(); }
            catch { }
        };
    }

    /// <summary>
    /// Snapshot of everything LoadSettingsAsync gathers off the UI thread.
    /// All disk reads (settings.json language, settings.json mode,
    /// config.json sync toggles) happen in Task.Run; the dispatcher
    /// continuation only mutates controls.
    /// </summary>
    private sealed record SettingsSnapshot(
        string Language,
        string? Mode,
        bool? SyncAchievements,
        bool? SyncPlaytime,
        bool? SyncLuas,
        bool? AutoUpdateDll,
        bool? ShowNonSteamGame,
        bool? ParentalIgnorePlaytime,
        bool? ParentalBypassPlaytime,
        bool? SchemaFetch,
        bool? OverrideNonStGate);

    // M15: Move language/mode/sync-toggle config reads off the UI thread.
    // Loaded used to call ReadLanguageSetting + ReadModeSetting +
    // LoadSyncToggles synchronously, which can stall on a slow disk
    // (network drive, AV scan). Mirror DashboardPage.LoadStatusAsync:
    // gather a snapshot in Task.Run, apply controls afterward.
    private async Task LoadSettingsAsync()
    {
        var snapshot = await Task.Run(() =>
        {
            var lang = ReadLanguageSetting();
            var mode = Services.SteamDetector.ReadModeSetting();

            bool? a = null, p = null, l = null, u = null, nsg = null, pip = null, pbp = null, sf = null, ovr = null;
            if (mode == "cloud_redirect")
                ReadSyncTogglesInto(ref a, ref p, ref l, ref u, ref nsg, ref pip, ref pbp, ref sf, ref ovr);
            else
                // STFixer mode exposes achievements, "Show Lua Game in Status" and
                // the Steam Family toggles; leave the cloud-only toggles untouched.
                ReadStFixerTogglesInto(ref sf, ref nsg, ref pip, ref pbp);

            return new SettingsSnapshot(lang, mode, a, p, l, u, nsg, pip, pbp, sf, ovr);
        });

        ApplySettingsSnapshot(snapshot);
    }

    private void ApplySettingsSnapshot(SettingsSnapshot snap)
    {
        ApplyLanguageSelector(snap.Language);
        _mode = snap.Mode;
        ApplyModeSelector(snap.Mode);

        // Quality of Life holds "Make Achievements Work" and "Show Lua Game in
        // Status", so it stays visible in both modes.
        ExtraSection.Visibility = Visibility.Visible;
        AchievementsSection.Visibility = Visibility.Visible;
        ShowNonSteamGameCard.Visibility = Visibility.Visible;
        // Steam Family (parental) toggles work in both modes; the DLL gates them on
        // cloudSaveOnly, not on app mode.
        ParentalSection.Visibility = Visibility.Visible;
        if (snap.Mode == "cloud_redirect")
        {
            ExperimentalSection.Visibility = Visibility.Visible;
            ApplySyncToggles(snap.SyncAchievements, snap.SyncPlaytime, snap.SyncLuas, snap.AutoUpdateDll,
                             snap.ShowNonSteamGame, snap.ParentalIgnorePlaytime, snap.ParentalBypassPlaytime,
                             snap.SchemaFetch, snap.OverrideNonStGate);
        }
        else
        {
            ExperimentalSection.Visibility = Visibility.Collapsed;
            // Auto-update, schema_fetch, show_non_steam_game and the parental toggles
            // are always-visible, so keep them in sync even when cloud-only sections hide.
            ApplySyncToggles(false, false, false, snap.AutoUpdateDll, snap.ShowNonSteamGame,
                             snap.ParentalIgnorePlaytime, snap.ParentalBypassPlaytime, snap.SchemaFetch, false);
        }
    }

    private void ApplyLanguageSelector(string saved)
    {
        _languageLoading = true;
        try
        {
            LanguageComboBox.Items.Clear();

            int selectedIndex = 0;
            for (int i = 0; i < LanguageOptions.Length; i++)
            {
                var (resKey, code) = LanguageOptions[i];
                LanguageComboBox.Items.Add(S.Get(resKey));
                if (code == saved)
                    selectedIndex = i;
            }

            LanguageComboBox.SelectedIndex = selectedIndex;
            _lastSavedLanguageIndex = selectedIndex;
        }
        finally
        {
            _languageLoading = false;
        }
    }

    private void ApplyModeSelector(string? mode)
    {
        _modeLoading = true;
        try
        {
            // Default to ST Fixer when no mode is set yet.
            var target = mode == "cloud_redirect" ? "cloud_redirect" : "stfixer";
            for (int i = 0; i < ModeComboBox.Items.Count; i++)
            {
                if (ModeComboBox.Items[i] is ComboBoxItem item && item.Tag as string == target)
                {
                    ModeComboBox.SelectedIndex = i;
                    break;
                }
            }
        }
        finally
        {
            _modeLoading = false;
        }
    }

    private async void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_modeLoading) return;
        if (ModeComboBox.SelectedItem is not ComboBoxItem item) return;

        var target = item.Tag as string ?? "stfixer";
        if (target == _mode) return;

        // Switching into Cloud Redirect shows the consent dialog once, ever.
        if (target == "cloud_redirect" && !Services.ModeService.HasAcceptedDisclaimer())
        {
            var disclaimer = new Windows.DisclaimerWindow { Owner = Window.GetWindow(this) };
            if (disclaimer.ShowDialog() != true || !disclaimer.Accepted)
            {
                ApplyModeSelector(_mode); // revert combo
                return;
            }
            Services.ModeService.MarkDisclaimerAccepted();
        }

        bool cloudEnabled = target == "cloud_redirect";
        try
        {
            Services.ModeService.PersistMode(target, cloudEnabled);
        }
        catch (Exception ex)
        {
            ApplyModeSelector(_mode); // revert combo on failure
            await Services.Dialog.ShowErrorAsync(
                S.Get("Common_Error"),
                S.Format("Choice_FailedSaveMode", ex.Message));
            return;
        }

        _mode = target;
        var mw = Window.GetWindow(this) as MainWindow;
        mw?.ApplyMode(target);
        // Re-read settings so this page's sections reflect the new mode.
        try { await LoadSettingsAsync(); } catch { }
    }

    private void ApplySyncToggles(bool? achievements, bool? playtime, bool? luas, bool? autoUpdateDll,
                                   bool? showNonSteamGame, bool? parentalIgnorePlaytime, bool? parentalBypassPlaytime,
                                   bool? schemaFetch, bool? overrideNonStGate)
    {
        _syncLoading = true;
        try
        {
            if (achievements == true) SyncAchievementsToggle.IsChecked = true;
            if (playtime == true) SyncPlaytimeToggle.IsChecked = true;
            if (luas == true) SyncLuasToggle.IsChecked = true;
            if (autoUpdateDll == true) AutoUpdateDllToggle.IsChecked = true;
            if (showNonSteamGame == true) ShowNonSteamGameToggle.IsChecked = true;
            if (parentalIgnorePlaytime == true) ParentalIgnorePlaytimeToggle.IsChecked = true;
            if (parentalBypassPlaytime == true) ParentalBypassPlaytimeToggle.IsChecked = true;
            if (schemaFetch == true) GetAchievementDataToggle.IsChecked = true;
            // override_non_st_client_gate has no UI; config value is preserved on write.
        }
        finally
        {
            _syncLoading = false;
        }
    }

    /// <summary>
    /// Reads the sync toggle booleans from config.json on the calling
    /// thread. Used by LoadSettingsAsync inside Task.Run so the dispatcher
    /// path never opens config.json synchronously.
    /// </summary>
    private static void ReadSyncTogglesInto(ref bool? achievements, ref bool? playtime, ref bool? luas, ref bool? autoUpdateDll,
                                              ref bool? showNonSteamGame, ref bool? parentalIgnorePlaytime, ref bool? parentalBypassPlaytime,
                                              ref bool? schemaFetch, ref bool? overrideNonStGate)
    {
        try
        {
            var path = GetConfigPath();
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("sync_achievements", out var a) && a.ValueKind == JsonValueKind.True)
                achievements = true;
            if (root.TryGetProperty("sync_playtime", out var p) && p.ValueKind == JsonValueKind.True)
                playtime = true;
            if (root.TryGetProperty("sync_luas", out var l) && l.ValueKind == JsonValueKind.True)
                luas = true;
            if (root.TryGetProperty("auto_update_dll", out var u))
                autoUpdateDll = u.ValueKind == JsonValueKind.True;
            else
                autoUpdateDll = true; // default on when key absent
            if (root.TryGetProperty("show_non_steam_game", out var nsg))
                showNonSteamGame = nsg.ValueKind == JsonValueKind.True;
            else
                showNonSteamGame = true; // default on when key absent
            if (root.TryGetProperty("parental_ignore_playtime", out var pip) && pip.ValueKind == JsonValueKind.True)
                parentalIgnorePlaytime = true;
            if (root.TryGetProperty("parental_bypass_playtime", out var pbp) && pbp.ValueKind == JsonValueKind.True)
                parentalBypassPlaytime = true;
            // Schema fetch: default ON when key absent (matches DLL default).
            if (root.TryGetProperty("schema_fetch", out var sf2) && sf2.ValueKind == JsonValueKind.False)
                schemaFetch = false;
            else if (root.TryGetProperty("experimental_schema_fetch", out var sf) && sf.ValueKind == JsonValueKind.False)
                schemaFetch = false;
            else
                schemaFetch = true;
            // UNSUPPORTED WIP OVERRIDE NON-ST CLIENT GATE: default off when absent.
            if (root.TryGetProperty("override_non_st_client_gate", out var ovr) && ovr.ValueKind == JsonValueKind.True)
                overrideNonStGate = true;
        }
        catch { }
    }

    /// <summary>Reads the STFixer-mode toggles visible in both modes (schema_fetch,
    /// show_non_steam_game, and the two parental toggles) from config.json.
    /// schema_fetch/show_non_steam_game default ON when absent (DLL defaults);
    /// parental toggles default OFF.</summary>
    private static void ReadStFixerTogglesInto(ref bool? schemaFetch, ref bool? showNonSteamGame,
                                               ref bool? parentalIgnorePlaytime, ref bool? parentalBypassPlaytime)
    {
        try
        {
            var path = GetConfigPath();
            if (!File.Exists(path)) { schemaFetch = true; showNonSteamGame = true; return; }

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("schema_fetch", out var sf2) && sf2.ValueKind == JsonValueKind.False)
                schemaFetch = false;
            else if (root.TryGetProperty("experimental_schema_fetch", out var sf) && sf.ValueKind == JsonValueKind.False)
                schemaFetch = false;
            else
                schemaFetch = true;

            if (root.TryGetProperty("show_non_steam_game", out var nsg))
                showNonSteamGame = nsg.ValueKind == JsonValueKind.True;
            else
                showNonSteamGame = true; // default on when key absent

            if (root.TryGetProperty("parental_ignore_playtime", out var pip) && pip.ValueKind == JsonValueKind.True)
                parentalIgnorePlaytime = true;
            if (root.TryGetProperty("parental_bypass_playtime", out var pbp) && pbp.ValueKind == JsonValueKind.True)
                parentalBypassPlaytime = true;
        }
        catch { }
    }

    private void LoadAbout()
    {
        // Prefer the informational version (carries any pre-release suffix like
        // "-TEST1"); strip build metadata after '+'. Fall back to the numeric
        // assembly version formatted as X.Y.Z.
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+');
            VersionText.Text = plus >= 0 ? informational.Substring(0, plus) : informational;
            return;
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version != null
            ? S.Format("Settings_VersionFormat", version.Major, version.Minor, version.Build)
            : S.Get("Settings_CloudRedirect");
    }

    private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_languageLoading) return;

        var idx = LanguageComboBox.SelectedIndex;
        if (idx < 0 || idx >= LanguageOptions.Length) return;

        var code = LanguageOptions[idx].Code;
        try
        {
            SaveLanguageSetting(code);
        }
        catch (Exception ex)
        {
            // Revert the combo to the last successfully-persisted index so
            // the user doesn't see a fake selection that won't survive
            // restart. Suppress the re-fire via _languageLoading.
            _languageLoading = true;
            try { LanguageComboBox.SelectedIndex = _lastSavedLanguageIndex; }
            finally { _languageLoading = false; }

            await Services.Dialog.ShowErrorAsync(
                S.Get("Common_Error"),
                S.Format("Settings_FailedSaveLanguage", ex.Message));
            return;
        }

        _lastSavedLanguageIndex = idx;
        LanguageHintText.Text = S.Get("Settings_RestartRequired");
        RestartButton.Visibility = Visibility.Visible;
    }

    private void RestartApp_Click(object sender, RoutedEventArgs e)
    {
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (exePath != null)
            Process.Start(exePath);
        Application.Current.Shutdown();
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(Services.SteamDetector.GetConfigDir(), "settings.json");
    }

    private static string ReadLanguageSetting()
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path)) return "system";

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("language", out var prop))
                return prop.GetString() ?? "system";
        }
        catch { }
        return "system";
    }

    /// <summary>
    /// Writes the language preference to settings.json. Throws on real
    /// I/O failure so the caller can revert the combo and surface the
    /// error; the inner old-file parse catch is intentional (corrupt
    /// settings → write fresh, preserving language only).
    /// </summary>
    private static void SaveLanguageSetting(string code)
    {
        var path = GetSettingsPath();
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Read existing settings to preserve other fields
        JsonElement existing = default;
        if (File.Exists(path))
        {
            try
            {
                var oldJson = File.ReadAllText(path);
                using var oldDoc = JsonDocument.Parse(oldJson);
                existing = oldDoc.RootElement.Clone();
            }
            catch { }
        }

        using var ms = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("language", code);

            // Copy any other properties from the existing file
            if (existing.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in existing.EnumerateObject())
                {
                    if (prop.Name == "language") continue;
                    prop.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        var newJson = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        Services.FileUtils.AtomicWriteAllText(path, newJson);
    }

    private static string GetConfigPath()
    {
        return Services.SteamDetector.GetConfigFilePath();
    }

    private async void SyncToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncLoading) return;

        try
        {
            SaveSyncToggles();
        }
        catch (Exception ex)
        {
            // Only the toggle that just fired diverges from the on-disk
            // value — flip it back and surface the error. The
            // _syncLoading guard suppresses the recursive Changed event
            // that the programmatic IsChecked set will trigger.
            if (sender is Wpf.Ui.Controls.ToggleSwitch toggle)
            {
                _syncLoading = true;
                try { toggle.IsChecked = !(toggle.IsChecked == true); }
                finally { _syncLoading = false; }
            }

            await Services.Dialog.ShowErrorAsync(
                S.Get("Common_Error"),
                S.Format("Settings_FailedSaveSync", ex.Message));
        }
    }

    /// <summary>
    /// Persists the three sync toggles into config.json via ConfigHelper
    /// (which preserves caller-unowned keys and atomic-writes). Throws on
    /// real I/O failure so the caller can revert UI state and surface
    /// the error.
    /// </summary>
    private void SaveSyncToggles()
    {
        var path = GetConfigPath();

        // Scope the save to keys owned by visible controls; persisting hidden toggles
        // would clobber the user's cloud_redirect-mode settings.
        if (_mode != "cloud_redirect")
        {
            Services.ConfigHelper.SaveConfig(path,
                new[] { "auto_update_dll", "schema_fetch", "experimental_schema_fetch", "show_non_steam_game",
                        "parental_ignore_playtime", "parental_bypass_playtime" },
                writer =>
                {
                    writer.WriteBoolean("auto_update_dll", AutoUpdateDllToggle.IsChecked == true);
                    writer.WriteBoolean("schema_fetch", GetAchievementDataToggle.IsChecked == true);
                    writer.WriteBoolean("show_non_steam_game", ShowNonSteamGameToggle.IsChecked == true);
                    writer.WriteBoolean("parental_ignore_playtime", ParentalIgnorePlaytimeToggle.IsChecked == true);
                    writer.WriteBoolean("parental_bypass_playtime", ParentalBypassPlaytimeToggle.IsChecked == true);
                });
            return;
        }

        Services.ConfigHelper.SaveConfig(path,
            new[] { "sync_achievements", "sync_playtime", "sync_luas", "auto_update_dll",
                    "show_non_steam_game", "parental_ignore_playtime", "parental_bypass_playtime",
                    "schema_fetch", "experimental_schema_fetch" },
            writer =>
            {
                writer.WriteBoolean("sync_achievements", SyncAchievementsToggle.IsChecked == true);
                writer.WriteBoolean("sync_playtime", SyncPlaytimeToggle.IsChecked == true);
                writer.WriteBoolean("sync_luas", SyncLuasToggle.IsChecked == true);
                writer.WriteBoolean("auto_update_dll", AutoUpdateDllToggle.IsChecked == true);
                writer.WriteBoolean("show_non_steam_game", ShowNonSteamGameToggle.IsChecked == true);
                writer.WriteBoolean("parental_ignore_playtime", ParentalIgnorePlaytimeToggle.IsChecked == true);
                writer.WriteBoolean("parental_bypass_playtime", ParentalBypassPlaytimeToggle.IsChecked == true);
                writer.WriteBoolean("schema_fetch", GetAchievementDataToggle.IsChecked == true);
                // override_non_st_client_gate has no UI; existing config value is preserved.
            });
    }

    private async void ResetData_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = await Services.Dialog.ConfirmDangerAsync(S.Get("Settings_ConfirmResetTitle"),
            S.Get("Settings_ConfirmResetMessage"));

        if (!confirmed) return;

        var steamPath = Services.SteamDetector.FindSteamPath();
        if (steamPath == null) return;

        try
        {
            var dataRoot = Path.Combine(steamPath, "cloud_redirect");
            var storagePath = Path.Combine(dataRoot, "storage");

            // Legacy/unused folders from older versions
            var blobsPath = Path.Combine(dataRoot, "blobs");
            var savesPath = Path.Combine(dataRoot, "saves");

            if (Directory.Exists(storagePath))
                Directory.Delete(storagePath, true);
            if (Directory.Exists(blobsPath))
                Directory.Delete(blobsPath, true);
            if (Directory.Exists(savesPath))
                Directory.Delete(savesPath, true);

            await Services.Dialog.ShowInfoAsync(S.Get("Settings_Done"), S.Get("Settings_ResetDoneMessage"));
        }
        catch (Exception ex)
        {
            await Services.Dialog.ShowErrorAsync(S.Get("Common_Error"), S.Format("Settings_FailedReset", ex.Message));
        }
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true })?.Dispose();
    }
}
