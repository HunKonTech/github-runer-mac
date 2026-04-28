using System.Collections.Immutable;
using GitHubRunnerTray.Core.Localization;
using GitHubRunnerTray.Core.Models;

namespace GitHubRunnerTray.Core.Services;

public class LocalizationService : ILocalizationService
{
    private static readonly ImmutableDictionary<string, string> _english = ImmutableDictionary<string, string>.Empty
        .Add(LocalizationKeys.AppName, "github runer mac")
        .Add(LocalizationKeys.StatusRunnerTitle, "Runner")
        .Add(LocalizationKeys.StatusActivityTitle, "Activity")
        .Add(LocalizationKeys.StatusNetworkTitle, "Network")
        .Add(LocalizationKeys.StatusModeTitle, "Mode")
        .Add(LocalizationKeys.StatusLaunchAtLoginTitle, "Launch at login")
        .Add(LocalizationKeys.ButtonManualStart, "Start manually")
        .Add(LocalizationKeys.ButtonManualStop, "Stop manually")
        .Add(LocalizationKeys.ButtonAutomaticMode, "Automatic mode")
        .Add(LocalizationKeys.ButtonRefresh, "Refresh")
        .Add(LocalizationKeys.ToggleLaunchAtLogin, "Launch automatically at login")
        .Add(LocalizationKeys.ButtonOpenLoginItemsSettings, "Open Login Items settings")
        .Add(LocalizationKeys.ButtonOpenRunnerDirectory, "Open runner folder")
        .Add(LocalizationKeys.ButtonChooseRunnerDirectory, "Choose folder")
        .Add(LocalizationKeys.ButtonOpenUpdateWindow, "Check for updates")
        .Add(LocalizationKeys.ButtonOpenAboutWindow, "About and links")
        .Add(LocalizationKeys.ButtonOpenSettingsWindow, "Settings...")
        .Add(LocalizationKeys.SettingsWindowTitle, "Settings")
        .Add(LocalizationKeys.SettingsGeneralTitle, "General")
        .Add(LocalizationKeys.SettingsRunnerTitle, "Runner")
        .Add(LocalizationKeys.SettingsUpdatesTitle, "Updates")
        .Add(LocalizationKeys.SettingsNetworkTitle, "Network")
        .Add(LocalizationKeys.SettingsAdvancedTitle, "Advanced")
        .Add(LocalizationKeys.SettingsAboutTitle, "About")
        .Add(LocalizationKeys.SettingsLanguageTitle, "Language")
        .Add(LocalizationKeys.LanguageSystemDefault, "System default")
        .Add(LocalizationKeys.LanguageHungarian, "Hungarian")
        .Add(LocalizationKeys.LanguageEnglish, "English")
        .Add(LocalizationKeys.SettingsLanguageRestartHint, "Language changes are saved immediately. Some text may update after restarting the app.")
        .Add(LocalizationKeys.SettingsRunnerFolderTitle, "Runner folder")
        .Add(LocalizationKeys.SettingsAutomaticUpdateCheckTitle, "Check for updates automatically")
        .Add(LocalizationKeys.SettingsUpdateChannelTitle, "Update channel")
        .Add(LocalizationKeys.UpdateChannelStable, "Stable")
        .Add(LocalizationKeys.UpdateChannelPreview, "Developer / Preview")
        .Add(LocalizationKeys.SettingsNetworkStateTitle, "Current network")
        .Add(LocalizationKeys.SettingsNetworkPolicyTitle, "Network policy")
        .Add(LocalizationKeys.SettingsNetworkOverrideTitle, "Manual override")
        .Add(LocalizationKeys.SettingsNetworkExplanation, "Automatic mode runs the runner on unmetered connections and stops it when the network is offline or metered.")
        .Add(LocalizationKeys.SettingsNetworkDecisionExplanation, "Unmetered -> run\nExpensive/offline -> stop\nUnknown -> keep current state")
        .Add(LocalizationKeys.SettingsAdvancedRunnerProcessTitle, "Runner process")
        .Add(LocalizationKeys.SettingsAdvancedCPU, "CPU")
        .Add(LocalizationKeys.SettingsAdvancedMemory, "Memory")
        .Add(LocalizationKeys.SettingsAdvancedJobActive, "Job active")
        .Add(LocalizationKeys.SettingsAdvancedLastRefresh, "Last refresh")
        .Add(LocalizationKeys.SettingsAboutAppNameTitle, "App name")
        .Add(LocalizationKeys.SettingsAboutLicenseTitle, "License")
        .Add(LocalizationKeys.SettingsAboutLicenseValue, "MIT")
        .Add(LocalizationKeys.AdvancedViewTitle, "Advanced view")
        .Add(LocalizationKeys.BooleanYes, "Yes")
        .Add(LocalizationKeys.BooleanNo, "No")
        .Add(LocalizationKeys.AboutWindowTitle, "About github runer mac")
        .Add(LocalizationKeys.AboutDescription, "Created by Benedek Koncsik. Open the profile pages or the project repository directly from the menu.")
        .Add(LocalizationKeys.ButtonOpenAuthorGitHub, "GitHub profile")
        .Add(LocalizationKeys.ButtonOpenAuthorX, "X profile")
        .Add(LocalizationKeys.ButtonOpenRepository, "Project repository")
        .Add(LocalizationKeys.UpdateWindowTitle, "Software update")
        .Add(LocalizationKeys.UpdateDescription, "Check the latest GitHub release and open the downloaded installer.")
        .Add(LocalizationKeys.UpdateInstalledVersionTitle, "Installed version")
        .Add(LocalizationKeys.UpdateLatestVersionTitle, "Latest version")
        .Add(LocalizationKeys.UpdateStatusTitle, "Status")
        .Add(LocalizationKeys.UpdateUnknownVersion, "Not checked yet")
        .Add(LocalizationKeys.ButtonCheckForUpdates, "Check now")
        .Add(LocalizationKeys.ButtonInstallUpdate, "Download installer")
        .Add(LocalizationKeys.ButtonOpenReleasePage, "Open release page")
        .Add(LocalizationKeys.UpdateIdle, "Ready to check for updates.")
        .Add(LocalizationKeys.UpdateChecking, "Checking GitHub releases...")
        .Add(LocalizationKeys.UpdateUpToDate, "You already have the latest version.")
        .Add(LocalizationKeys.UpdateAvailableFallback, "A newer version is available.")
        .Add(LocalizationKeys.UpdateDownloading, "Downloading the new version...")
        .Add(LocalizationKeys.UpdateInstalling, "Opening the downloaded installer...")
        .Add(LocalizationKeys.UpdateErrorInvalidResponse, "The update server returned an unexpected response.")
        .Add(LocalizationKeys.UpdateErrorNoPublishedRelease, "No published GitHub release is available yet for this repository.")
        .Add(LocalizationKeys.UpdateErrorMissingAsset, "The latest release does not contain a platform app download.")
        .Add(LocalizationKeys.UpdateErrorDownload, "The update package could not be downloaded.")
        .Add(LocalizationKeys.UpdateErrorOpenInstaller, "The downloaded installer could not be opened.")
        .Add(LocalizationKeys.UpdateErrorNoRelease, "There is no downloadable release selected.")
        .Add(LocalizationKeys.ButtonQuit, "Quit")
        .Add(LocalizationKeys.RunnerRunning, "Running")
        .Add(LocalizationKeys.RunnerStopped, "Stopped")
        .Add(LocalizationKeys.ControlModeAutomatic, "Automatic")
        .Add(LocalizationKeys.ControlModeForceRunning, "Forced running")
        .Add(LocalizationKeys.ControlModeForceStopped, "Forced stopped")
        .Add(LocalizationKeys.PolicyAutomaticRun, "In automatic mode the runner may run.")
        .Add(LocalizationKeys.PolicyAutomaticExpensive, "In automatic mode the runner stops on a metered connection.")
        .Add(LocalizationKeys.PolicyAutomaticOffline, "In automatic mode the runner stops when there is no internet.")
        .Add(LocalizationKeys.PolicyAutomaticUnknown, "In automatic mode the app is still checking the network.")
        .Add(LocalizationKeys.PolicyForceRunning, "Manual override keeps the runner running regardless of network rules.")
        .Add(LocalizationKeys.PolicyForceStopped, "Manual override keeps the runner stopped.")
        .Add(LocalizationKeys.LaunchAtLoginEnabled, "Enabled")
        .Add(LocalizationKeys.LaunchAtLoginRequiresApproval, "Approval required")
        .Add(LocalizationKeys.LaunchAtLoginDisabled, "Disabled")
        .Add(LocalizationKeys.LaunchAtLoginUnavailable, "Unavailable in this build")
        .Add(LocalizationKeys.LaunchAtLoginUnknown, "Unknown")
        .Add(LocalizationKeys.ActivityWorkingJob, "Working: {0}")
        .Add(LocalizationKeys.ActivityWaitingForJob, "Waiting for jobs")
        .Add(LocalizationKeys.ActivityStopping, "Stopping")
        .Add(LocalizationKeys.ActivityWaitingOrStarting, "Waiting or starting")
        .Add(LocalizationKeys.ActivityRunnerStopped, "The runner is stopped")
        .Add(LocalizationKeys.ActivityUnknown, "Unknown status")
        .Add(LocalizationKeys.NetworkChecking, "Checking network...")
        .Add(LocalizationKeys.NetworkNoInternet, "No internet connection")
        .Add(LocalizationKeys.NetworkMetered, "{0}, metered")
        .Add(LocalizationKeys.NetworkUnmetered, "{0}, unmetered")
        .Add(LocalizationKeys.InterfaceEthernet, "Ethernet")
        .Add(LocalizationKeys.InterfaceWiFi, "Wi-Fi")
        .Add(LocalizationKeys.InterfaceCellular, "Cellular")
        .Add(LocalizationKeys.InterfaceOther, "Other connection")
        .Add(LocalizationKeys.InterfaceGeneric, "Connection")
        .Add(LocalizationKeys.ErrorMissingRunnerScript, "Cannot find the runner startup script: {0}")
        .Add(LocalizationKeys.ErrorLaunchAtLoginUpdate, "Failed to update launch at login: {0}")
        .Add(LocalizationKeys.ErrorRunnerHandling, "Failed to manage the runner: {0}")
        .Add(LocalizationKeys.SettingsStopOnBatteryTitle, "Pause runner on battery")
        .Add(LocalizationKeys.SettingsStopOnBatteryExplanation, "Automatically stop the runner when running on battery power and resume when connected to power.")
        .Add(LocalizationKeys.SettingsStopOnBatteryUnavailable, "Not available on this device");

    private static readonly ImmutableDictionary<string, string> _hungarian = ImmutableDictionary<string, string>.Empty
        .Add(LocalizationKeys.AppName, "github runer mac")
        .Add(LocalizationKeys.StatusRunnerTitle, "Runner")
        .Add(LocalizationKeys.StatusActivityTitle, "Munka")
        .Add(LocalizationKeys.StatusNetworkTitle, "Hálózat")
        .Add(LocalizationKeys.StatusModeTitle, "Mód")
        .Add(LocalizationKeys.StatusLaunchAtLoginTitle, "Induláskor")
        .Add(LocalizationKeys.ButtonManualStart, "Indítás kézzel")
        .Add(LocalizationKeys.ButtonManualStop, "Leállítás kézzel")
        .Add(LocalizationKeys.ButtonAutomaticMode, "Automatikus mód")
        .Add(LocalizationKeys.ButtonRefresh, "Frissítés")
        .Add(LocalizationKeys.ToggleLaunchAtLogin, "Induljon automatikusan bejelentkezéskor")
        .Add(LocalizationKeys.ButtonOpenLoginItemsSettings, "Login Items beállítások megnyitása")
        .Add(LocalizationKeys.ButtonOpenRunnerDirectory, "Runner mappa megnyitása")
        .Add(LocalizationKeys.ButtonChooseRunnerDirectory, "Mappa kiválasztása")
        .Add(LocalizationKeys.ButtonOpenUpdateWindow, "Frissítések keresése")
        .Add(LocalizationKeys.ButtonOpenAboutWindow, "Névjegy és linkek")
        .Add(LocalizationKeys.ButtonOpenSettingsWindow, "Beállítások...")
        .Add(LocalizationKeys.SettingsWindowTitle, "Beállítások")
        .Add(LocalizationKeys.SettingsGeneralTitle, "Általános")
        .Add(LocalizationKeys.SettingsRunnerTitle, "Runner")
        .Add(LocalizationKeys.SettingsUpdatesTitle, "Frissítések")
        .Add(LocalizationKeys.SettingsNetworkTitle, "Hálózat")
        .Add(LocalizationKeys.SettingsAdvancedTitle, "Haladó")
        .Add(LocalizationKeys.SettingsAboutTitle, "Névjegy")
        .Add(LocalizationKeys.SettingsLanguageTitle, "Nyelv")
        .Add(LocalizationKeys.LanguageSystemDefault, "Rendszer alapértelmezése")
        .Add(LocalizationKeys.LanguageHungarian, "Magyar")
        .Add(LocalizationKeys.LanguageEnglish, "Angol")
        .Add(LocalizationKeys.SettingsLanguageRestartHint, "A nyelvválasztást az app azonnal menti. Néhány szöveg csak az app újraindítása után frissülhet.")
        .Add(LocalizationKeys.SettingsRunnerFolderTitle, "Runner mappa")
        .Add(LocalizationKeys.SettingsAutomaticUpdateCheckTitle, "Frissítések automatikus keresése")
        .Add(LocalizationKeys.SettingsUpdateChannelTitle, "Frissítési csatorna")
        .Add(LocalizationKeys.UpdateChannelStable, "Stabil")
        .Add(LocalizationKeys.UpdateChannelPreview, "Fejlesztői / előnézeti")
        .Add(LocalizationKeys.SettingsNetworkStateTitle, "Aktuális hálózat")
        .Add(LocalizationKeys.SettingsNetworkPolicyTitle, "Hálózati szabály")
        .Add(LocalizationKeys.SettingsNetworkOverrideTitle, "Kézi felülbírálás")
        .Add(LocalizationKeys.SettingsNetworkExplanation, "Automatikus módban a runner nem forgalomkorlátos kapcsolaton futhat, offline vagy forgalomkorlátos hálózaton pedig leáll.")
        .Add(LocalizationKeys.SettingsNetworkDecisionExplanation, "Nem forgalomkorlátos -> futtatás\nForgalomkorlátos/offline -> leállítás\nIsmeretlen -> aktuális állapot megtartása")
        .Add(LocalizationKeys.SettingsAdvancedRunnerProcessTitle, "Runner folyamat")
        .Add(LocalizationKeys.SettingsAdvancedCPU, "CPU")
        .Add(LocalizationKeys.SettingsAdvancedMemory, "Memória")
        .Add(LocalizationKeys.SettingsAdvancedJobActive, "Aktív job")
        .Add(LocalizationKeys.SettingsAdvancedLastRefresh, "Utolsó frissítés")
        .Add(LocalizationKeys.SettingsAboutAppNameTitle, "App neve")
        .Add(LocalizationKeys.SettingsAboutLicenseTitle, "Licenc")
        .Add(LocalizationKeys.SettingsAboutLicenseValue, "MIT")
        .Add(LocalizationKeys.AdvancedViewTitle, "Haladó nézet")
        .Add(LocalizationKeys.BooleanYes, "Igen")
        .Add(LocalizationKeys.BooleanNo, "Nem")
        .Add(LocalizationKeys.AboutWindowTitle, "github runer mac névjegy")
        .Add(LocalizationKeys.AboutDescription, "Készítette Benedek Koncsik. Innen közvetlenül megnyithatod a profiloldalakat és a projekt repositoryját.")
        .Add(LocalizationKeys.ButtonOpenAuthorGitHub, "GitHub profil")
        .Add(LocalizationKeys.ButtonOpenAuthorX, "X profil")
        .Add(LocalizationKeys.ButtonOpenRepository, "Projekt repository")
        .Add(LocalizationKeys.UpdateWindowTitle, "Szoftverfrissítés")
        .Add(LocalizationKeys.UpdateDescription, "Ellenőrzi a legfrissebb GitHub release-t, letölti és megnyitja a telepítőt.")
        .Add(LocalizationKeys.UpdateInstalledVersionTitle, "Telepített verzió")
        .Add(LocalizationKeys.UpdateLatestVersionTitle, "Legfrissebb verzió")
        .Add(LocalizationKeys.UpdateStatusTitle, "Állapot")
        .Add(LocalizationKeys.UpdateUnknownVersion, "Még nincs ellenőrizve")
        .Add(LocalizationKeys.ButtonCheckForUpdates, "Ellenőrzés")
        .Add(LocalizationKeys.ButtonInstallUpdate, "Telepítő letöltése")
        .Add(LocalizationKeys.ButtonOpenReleasePage, "Release oldal megnyitása")
        .Add(LocalizationKeys.UpdateIdle, "Készen áll a frissítések ellenőrzésére.")
        .Add(LocalizationKeys.UpdateChecking, "GitHub release-ek ellenőrzése...")
        .Add(LocalizationKeys.UpdateUpToDate, "Már a legfrissebb verzió van telepítve.")
        .Add(LocalizationKeys.UpdateAvailableFallback, "Elérhető egy újabb verzió.")
        .Add(LocalizationKeys.UpdateDownloading, "Az új verzió letöltése folyamatban...")
        .Add(LocalizationKeys.UpdateInstalling, "A letöltött telepítő megnyitása...")
        .Add(LocalizationKeys.UpdateErrorInvalidResponse, "A frissítési kiszolgáló váratlan választ adott.")
        .Add(LocalizationKeys.UpdateErrorNoPublishedRelease, "Ehhez a repositoryhoz még nincs közzétett GitHub release.")
        .Add(LocalizationKeys.UpdateErrorMissingAsset, "A legfrissebb release nem tartalmaz letölthető alkalmazást.")
        .Add(LocalizationKeys.UpdateErrorDownload, "Nem sikerült letölteni a frissítő csomagot.")
        .Add(LocalizationKeys.UpdateErrorOpenInstaller, "Nem sikerült megnyitni a letöltött telepítőt.")
        .Add(LocalizationKeys.UpdateErrorNoRelease, "Nincs kiválasztott letölthető release.")
        .Add(LocalizationKeys.ButtonQuit, "Kilépés")
        .Add(LocalizationKeys.RunnerRunning, "Fut")
        .Add(LocalizationKeys.RunnerStopped, "Leállítva")
        .Add(LocalizationKeys.ControlModeAutomatic, "Automatikus")
        .Add(LocalizationKeys.ControlModeForceRunning, "Kézileg fut")
        .Add(LocalizationKeys.ControlModeForceStopped, "Kézileg leállítva")
        .Add(LocalizationKeys.PolicyAutomaticRun, "Automatikus módban a runner futhat.")
        .Add(LocalizationKeys.PolicyAutomaticExpensive, "Automatikus módban a runner megáll, mert a kapcsolat korlátos.")
        .Add(LocalizationKeys.PolicyAutomaticOffline, "Automatikus módban a runner megáll, mert nincs internet.")
        .Add(LocalizationKeys.PolicyAutomaticUnknown, "Automatikus módban a hálózat vizsgálata folyamatban van.")
        .Add(LocalizationKeys.PolicyForceRunning, "Kézi felülbírálattal fut, a hálózati szabály most nem állítja le.")
        .Add(LocalizationKeys.PolicyForceStopped, "Kézi felülbírálattal leállítva marad.")
        .Add(LocalizationKeys.LaunchAtLoginEnabled, "Engedélyezve")
        .Add(LocalizationKeys.LaunchAtLoginRequiresApproval, "Jóváhagyás szükséges")
        .Add(LocalizationKeys.LaunchAtLoginDisabled, "Kikapcsolva")
        .Add(LocalizationKeys.LaunchAtLoginUnavailable, "Ebben a buildben nem érhető el")
        .Add(LocalizationKeys.LaunchAtLoginUnknown, "Ismeretlen")
        .Add(LocalizationKeys.ActivityWorkingJob, "Dolgozik: {0}")
        .Add(LocalizationKeys.ActivityWaitingForJob, "Várakozik feladatra")
        .Add(LocalizationKeys.ActivityStopping, "Leállás folyamatban")
        .Add(LocalizationKeys.ActivityWaitingOrStarting, "Várakozik vagy indul")
        .Add(LocalizationKeys.ActivityRunnerStopped, "A runner le van állítva")
        .Add(LocalizationKeys.ActivityUnknown, "Állapot ismeretlen")
        .Add(LocalizationKeys.NetworkChecking, "Hálózat ellenőrzése...")
        .Add(LocalizationKeys.NetworkNoInternet, "Nincs elérhető internet")
        .Add(LocalizationKeys.NetworkMetered, "{0}, forgalomkorlátos")
        .Add(LocalizationKeys.NetworkUnmetered, "{0}, nem forgalomkorlátos")
        .Add(LocalizationKeys.InterfaceEthernet, "Ethernet")
        .Add(LocalizationKeys.InterfaceWiFi, "Wi-Fi")
        .Add(LocalizationKeys.InterfaceCellular, "Mobilhálózat")
        .Add(LocalizationKeys.InterfaceOther, "Másik kapcsolat")
        .Add(LocalizationKeys.InterfaceGeneric, "Kapcsolat")
        .Add(LocalizationKeys.ErrorMissingRunnerScript, "Nem találom a runner indító scriptet: {0}")
        .Add(LocalizationKeys.ErrorLaunchAtLoginUpdate, "Az automatikus indítás beállítása nem sikerült: {0}")
        .Add(LocalizationKeys.ErrorRunnerHandling, "Nem sikerült a runner kezelése: {0}")
        .Add(LocalizationKeys.SettingsStopOnBatteryTitle, "Runner szüneteltetése akkumulátoron")
        .Add(LocalizationKeys.SettingsStopOnBatteryExplanation, "Automatikusan leállítja a runnert, amikor akkumulátorról fut, és folytatja, amikor áramforrásra van csatlakoztatva.")
        .Add(LocalizationKeys.SettingsStopOnBatteryUnavailable, "Nem érhető el ezen az eszközön");

    private static readonly ImmutableDictionary<string, ImmutableDictionary<string, string>> _catalogs =
        ImmutableDictionary<string, ImmutableDictionary<string, string>>.Empty
            .Add("en", _english)
            .Add("hu", _hungarian);

    private static readonly string[] _supportedLanguageIdentifiers =
    [
        "en", "en-AU", "en-CA", "en-GB", "en-US", "hu"
    ];

    private AppLanguage _currentLanguage = AppLanguage.System;

    public event EventHandler? LanguageChanged;

    public AppLanguage CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                LanguageChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string Get(string key)
    {
        var catalog = GetActiveCatalog();
        return catalog.TryGetValue(key, out var value) ? value : key;
    }

    public string Get(string key, params object[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }

    private ImmutableDictionary<string, string> GetActiveCatalog()
    {
        var identifier = GetActiveLanguageIdentifier();
        return _catalogs.TryGetValue(identifier, out var catalog) ? catalog : _english;
    }

    private string GetActiveLanguageIdentifier()
    {
        if (_currentLanguage == AppLanguage.Hungarian) return "hu";
        if (_currentLanguage == AppLanguage.English) return "en";

        return SystemPreferredLanguageIdentifier();
    }

    private static string SystemPreferredLanguageIdentifier()
    {
        var systemLang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return _supportedLanguageIdentifiers.Contains(systemLang) ? systemLang : "en";
    }
}
