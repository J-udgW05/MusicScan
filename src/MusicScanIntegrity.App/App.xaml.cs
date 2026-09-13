using System.IO;
using System.Windows;
using System.Windows.Threading;
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
using MusicScanIntegrity.Core.Scanning;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.App;

/// <summary>Точка входа приложения: сборка зависимостей, запуск и корректное завершение.</summary>
public partial class App : Application
{
    private readonly IHost _host;

    /// <summary>Создаёт приложение и собирает контейнер зависимостей.</summary>
    public App()
    {
        // Журнала в программе нет: ничего не пишется ни на экран, ни на диск.
        // Об ошибках говорят диалоги с технической причиной, поэтому провайдеры
        // журнала хоста здесь просто не подключаются.
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();
    }

    /// <summary>Настраивает зависимости. Вынесено отдельно, чтобы состав был виден целиком.</summary>
    private void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddSingleton<ISettingsService>(_ => new JsonSettingsService());

        // Ядро проверки.
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

        // Вопросы о занятых файлах задаёт главное окно, но движок создаётся раньше
        // него — прямая зависимость дала бы цикл. Переходник разрывает его:
        // MainViewModel подставляет себя в Target в своём конструкторе.
        services.AddSingleton<LockedFileDecisionRelay>();
        services.AddSingleton<ILockedFileDecisionProvider>(p => p.GetRequiredService<LockedFileDecisionRelay>());
        services.AddSingleton<IScanEngine, ScanEngine>();
        services.AddSingleton<MainViewModel>();

        // Интерфейс.
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

        // Непойманное исключение не должно ронять программу молча.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        await _host.StartAsync();

        ISettingsService settings = _host.Services.GetRequiredService<ISettingsService>();

        bool firstRun = await settings.LoadAsync();

        // Эффекты, о которых человек ещё не высказывался, берутся из системы —
        // и тут же записываются в файл. Дальше решает файл: если в Windows
        // потом что-то переключат, программу это уже не касается.
        if (await ResolveVisualEffectsAsync(settings))
        {
            await settings.SaveAsync();
        }

        _host.Services.GetRequiredService<IThemeService>().Apply(settings.Current);

        // Осиротевшие временные копии от аварийно завершённого запуска
        // подчищаются на старте (03_IMPLEMENTATION_GUIDE.md, раздел 2).
        _host.Services.GetRequiredService<ITempCopyManager>().CleanupOrphans();

        // Механизм декодирования запускается один раз: без него проверка невозможна,
        // но программа при этом должна открыться и честно сказать, что не так.
        string? audioError = _host.Services.GetRequiredService<IAudioProbe>().Initialize();

        MainWindow window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;

        // Подложка ставится до показа: если сделать это после, окно успевает
        // мигнуть непрозрачным фоном.
        _host.Services.GetRequiredService<IThemeService>().ApplyToWindow(window, withBackdrop: true);
        window.Show();

        if (audioError is not null)
        {
            await _host.Services.GetRequiredService<IDialogService>().ShowMessageAsync(
                "Проверять файлы пока нельзя",
                "Не запустился механизм декодирования аудио. Программа откроется, но проверка работать не будет.",
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

            // Порядок здесь существенный. Сначала останавливается проверка,
            // и только потом освобождается механизм декодирования: Bass.Free
            // обрывает библиотеку целиком, и если рабочий поток в этот момент
            // ещё читает файл, программа уходит не с исключением, а вместе
            // с процессом. Раньше эти две строки стояли наоборот.
            (_host.Services.GetRequiredService<IScanEngine>() as IDisposable)?.Dispose();
            (_host.Services.GetRequiredService<IAudioProbe>() as IDisposable)?.Dispose();
            _host.Services.GetRequiredService<MainViewModel>().Dispose();
            (_host.Services.GetRequiredService<IThemeService>() as IDisposable)?.Dispose();

            // Даём фоновым задачам разумное время завершиться, но не ждём вечно.
            using CancellationTokenSource shutdown = new(TimeSpan.FromSeconds(5));
            await _host.StopAsync(shutdown.Token);
        }
        catch (Exception)
        {
            // Закрытие не должно падать — программа уже уходит.
        }
        finally
        {
            _host.Dispose();
            base.OnExit(e);
        }
    }

    /// <summary>
    /// Заполняет настройки эффектов, о которых ещё не спрашивали.
    /// </summary>
    /// <param name="settings">Служба настроек.</param>
    /// <returns><see langword="true" />, если что-то заполнили и надо сохранить.</returns>
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

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            MessageBox.Show(
                "Произошла непредвиденная ошибка. Программа продолжит работу, " +
                "но что-то могло не сработать.\n\n" + e.Exception.Message,
                "Music Scan Integrity",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception)
        {
            // Показать сообщение не удалось — молча продолжаем.
        }

        // Программа не закрывается: одна ошибка не повод терять результаты.
        e.Handled = true;
    }

    /// <summary>
    /// Ошибка в фоновом потоке процесс не переживёт, поэтому единственное, что
    /// здесь можно успеть, — сказать пользователю, отчего программа закрылась.
    /// </summary>
    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            string message = (e.ExceptionObject as Exception)?.Message ?? "Неизвестная ошибка";

            MessageBox.Show(
                "Произошла ошибка, из-за которой программа закрывается." +
                Environment.NewLine + Environment.NewLine + message,
                "Music Scan Integrity",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // Показать сообщение не удалось — процесс всё равно уже завершается.
        }
    }
}
