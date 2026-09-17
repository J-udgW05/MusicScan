using System.Globalization;
using System.IO;
using System.Windows.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MusicScanIntegrity.App.Services;
using MusicScanIntegrity.App.ViewModels;
using MusicScanIntegrity.App.Views;
using MusicScanIntegrity.Core.Audio;
using MusicScanIntegrity.Core.Discovery;
using MusicScanIntegrity.Core.History;
using MusicScanIntegrity.Core.Integrity;
using MusicScanIntegrity.Core.Locking;
using MusicScanIntegrity.Core.Playlists;
using MusicScanIntegrity.Core.Reporting;
using MusicScanIntegrity.Core.Resources;
using MusicScanIntegrity.Core.Scanning;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.App;

/// <summary>Application entry point: composition, startup and orderly shutdown.</summary>
public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        // The app keeps no log; errors surface through dialogs with the technical
        // cause, so host logging providers are not registered.
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();
    }

    /// <summary>Registers services; kept separate so the whole composition is visible at once.</summary>
    private void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddSingleton<ISettingsService>(_ => new JsonSettingsService());

        // Scanning core.
        services.AddSingleton<IAudioProbe, BassAudioProbe>();
        services.AddSingleton<IMetadataReader, TagLibMetadataReader>();
        services.AddSingleton<ILockOwnerDetector, RestartManagerLockDetector>();
        services.AddSingleton<ITempCopyManager, TempCopyManager>();
        services.AddSingleton<IFileDiscoveryService, FileDiscoveryService>();
        services.AddSingleton<IContainerIntegrityChecker, ContainerIntegrityChecker>();
        services.AddSingleton<IScanHistory, ScanHistory>();
        services.AddSingleton<IFileChecker, FileChecker>();

        services.AddSingleton<IPlaylistParser, M3uPlaylistParser>();
        services.AddSingleton<IPlaylistParser, PlsPlaylistParser>();
        services.AddSingleton<IPlaylistParser, CuePlaylistParser>();
        services.AddSingleton<IPlaylistService, PlaylistService>();

        services.AddSingleton<IReportExporter, HtmlReportExporter>();
        services.AddSingleton<IReportExporter, CsvReportExporter>();
        services.AddSingleton<IReportExporter, TextReportExporter>();
        services.AddSingleton<IReportService, ReportService>();

        // The main window asks the locked-file questions, but the engine is built
        // before it, so a direct dependency would be a cycle. The relay breaks it:
        // MainViewModel installs itself as Target in its constructor.
        services.AddSingleton<LockedFileDecisionRelay>();
        services.AddSingleton<ILockedFileDecisionProvider>(p => p.GetRequiredService<LockedFileDecisionRelay>());
        services.AddSingleton<IScanEngine, ScanEngine>();
        services.AddSingleton<MainViewModel>();

        // UI.
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ResultsViewModel>();
        services.AddSingleton<ReportViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>();
    }

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        UiScale.RegisterPopups();

        // An unhandled exception must not take the app down silently.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        await _host.StartAsync();

        ISettingsService settings = _host.Services.GetRequiredService<ISettingsService>();

        // Captured before the language is applied, which overwrites the UI culture.
        CultureInfo systemCulture = CultureInfo.CurrentUICulture;

        bool firstRun = await settings.LoadAsync();

        // Pick the language before anything is shown. Like the effects below,
        // a first-run choice is written back so the file decides from then on.
        if (settings.Current.Language is null)
        {
            settings.Current.Language = AppLanguage.Resolve(null, systemCulture.TwoLetterISOLanguageName, SystemRegion());
            await settings.SaveAsync();
        }

        UiLanguage.Apply(settings.Current.Language);

        // Effects the user has not decided on yet are taken from the system and
        // written straight to the settings file. From then on the file decides.
        if (await ResolveVisualEffectsAsync(settings))
        {
            await settings.SaveAsync();
        }

        _host.Services.GetRequiredService<IThemeService>().Apply(settings.Current);

        // Clean up temporary copies orphaned by a crashed previous run.
        _host.Services.GetRequiredService<ITempCopyManager>().CleanupOrphans();

        // Start the decoder once. Scanning is impossible without it, but the app
        // must still open and explain what went wrong.
        string? audioError = _host.Services.GetRequiredService<IAudioProbe>().Initialize();

        MainWindow window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;

        // Apply the backdrop before showing the window, or it flashes an opaque
        // background.
        _host.Services.GetRequiredService<IThemeService>().ApplyToWindow(window, withBackdrop: true);
        window.Show();

        if (audioError is not null)
        {
            await _host.Services.GetRequiredService<IDialogService>().ShowMessageAsync(
                Strings.App_NoAudio_Title,
                Strings.App_NoAudio_Text,
                audioError,
                isError: true);
        }

        MainViewModel viewModel = _host.Services.GetRequiredService<MainViewModel>();

        if (firstRun && settings.Current.ShowFirstRunTip)
        {
            bool doNotShowAgain = _host.Services.GetRequiredService<IDialogService>()
                .ShowFirstRun(out bool openFolderPicker);

            if (doNotShowAgain)
            {
                settings.Current.ShowFirstRunTip = false;
                await settings.SaveAsync();
            }

            if (openFolderPicker && viewModel.PickFolderCommand.CanExecute(null))
            {
                viewModel.PickFolderCommand.Execute(null);
            }
        }
        else if (settings.Current.RememberLastFolder &&
                 !string.IsNullOrWhiteSpace(settings.Current.LastFolder) &&
                 Directory.Exists(settings.Current.LastFolder))
        {
            await viewModel.SetFolderAsync(settings.Current.LastFolder);
        }
    }

    /// <inheritdoc />
    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            await _host.Services.GetRequiredService<ISettingsService>().SaveAsync();

            // Order matters: stop the scan before freeing the decoder. Bass.Free tears
            // the library down whole, and a worker still reading a file takes the
            // process with it rather than throwing. These lines used to be reversed.
            (_host.Services.GetRequiredService<IScanEngine>() as IDisposable)?.Dispose();
            (_host.Services.GetRequiredService<IAudioProbe>() as IDisposable)?.Dispose();
            _host.Services.GetRequiredService<MainViewModel>().Dispose();
            (_host.Services.GetRequiredService<IThemeService>() as IDisposable)?.Dispose();

            // Give background tasks reasonable time to finish, but do not wait forever.
            using CancellationTokenSource shutdown = new(TimeSpan.FromSeconds(5));
            await _host.StopAsync(shutdown.Token);
        }
        catch (Exception)
        {
            // Shutdown must not throw; the app is exiting anyway.
        }
        finally
        {
            _host.Dispose();
            base.OnExit(e);
        }
    }

    /// <summary>Fills in effect settings the user has never chosen.</summary>
    /// <returns><see langword="true" /> when something was filled and needs saving.</returns>
    private static Task<bool> ResolveVisualEffectsAsync(ISettingsService settings)
    {
        AppSettings current = settings.Current;

        if (current.MicaEffect is not null && current.Animations is not null)
        {
            return Task.FromResult(false);
        }

        SystemEffects system = SystemEffectsReader.Read();

        current.MicaEffect = VisualEffects.ResolveMicaPreference(current.MicaEffect, system);
        current.Animations = VisualEffects.ResolveAnimations(current.Animations, system);

        return Task.FromResult(true);
    }

    private static string? SystemRegion()
    {
        try
        {
            return RegionInfo.CurrentRegion.TwoLetterISORegionName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            MessageBox.Show(
                Strings.App_UnexpectedError + "\n\n" + e.Exception.Message,
                "Music Scan Integrity",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception)
        {
            // Could not show the message; carry on.
        }

        // Keep running: one error is no reason to lose the results.
        e.Handled = true;
    }

    /// <summary>
    /// The process will not survive an exception on a background thread; all that
    /// can be done is tell the user why the app is closing.
    /// </summary>
    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            string message = (e.ExceptionObject as Exception)?.Message ?? Strings.App_UnknownError;

            MessageBox.Show(
                Strings.App_FatalError +
                Environment.NewLine + Environment.NewLine + message,
                "Music Scan Integrity",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // Could not show the message; the process is terminating anyway.
        }
    }
}
