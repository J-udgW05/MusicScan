using System.Windows;
using System.Windows.Threading;
using MusicScanIntegrity.App.Services;
using MusicScanIntegrity.App.ViewModels;
using MusicScanIntegrity.Core.History;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Reporting;
using MusicScanIntegrity.Core.Settings;
using Xunit;

namespace MusicScanIntegrity.App.Tests;

/// <summary>
/// Быстрая панель на экране проверки и экран настроек правят одни и те же
/// параметры. Раньше экран настроек снимал их копию один раз при запуске:
/// переключение в панели он не замечал, а собственное применение возвращало
/// устаревшее значение обратно.
/// </summary>
public sealed class SettingsSyncTests
{
    [Fact]
    public void Экран_настроек_подхватывает_изменение_извне()
    {
        Sta.Run(() =>
        {
            FakeSettings settings = new();
            SettingsViewModel viewModel = Create(settings);

            Assert.True(viewModel.CheckMetadata);

            // Так это делает быстрая панель: правит текущие настройки и сообщает.
            AppSettings changed = settings.Current.Clone();
            changed.CheckMetadata = false;
            settings.ApplyAsync(changed).GetAwaiter().GetResult();
            Pump();

            Assert.False(viewModel.CheckMetadata);
        });
    }

    [Fact]
    public void Применение_настроек_не_возвращает_изменение_извне()
    {
        Sta.Run(() =>
        {
            FakeSettings settings = new();
            SettingsViewModel viewModel = Create(settings);

            AppSettings changed = settings.Current.Clone();
            changed.CheckMetadata = false;
            settings.ApplyAsync(changed).GetAwaiter().GetResult();
            Pump();

            // Пользователь меняет в настройках что-то другое и применяет.
            viewModel.ShowStatusBar = !viewModel.ShowStatusBar;
            viewModel.ApplyAsync().GetAwaiter().GetResult();
            Pump();

            Assert.False(settings.Current.CheckMetadata);
        });
    }

    private static SettingsViewModel Create(ISettingsService settings) =>
        new(settings, new FakeTheme(), new FakeDialogs(), new FakeHistory());

    /// <summary>Прокручивает очередь диспетчера: перечитывание идёт через неё.</summary>
    private static void Pump()
    {
        DispatcherFrame frame = new();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; private set; } = new() { CheckMetadata = true };

        public string SettingsFilePath => "память";

        public event EventHandler<AppSettings>? Changed;

        public Task<bool> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task ApplyAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            Changed?.Invoke(this, settings);
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            Current = new AppSettings();
            Changed?.Invoke(this, Current);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTheme : IThemeService
    {
        public AppTheme EffectiveTheme => AppTheme.Light;

        public bool IsMicaSupported => false;

        public void Apply(AppSettings settings) { }

        public void ApplyToWindow(Window window, bool withBackdrop) { }

        public string DefaultColorHex(CheckStatus status) => "#000000";
    }

    private sealed class FakeDialogs : IDialogService
    {
        public string? PickFolder(string title, string? initialFolder) => null;

        public string? PickSaveFile(string title, string filter, string suggestedName, string? initialFolder) => null;

        public string? PickColor(string title, string currentHex) => null;

        public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText,
            bool destructive = false, string? copyPath = null, string? technicalDetail = null, bool isError = false) =>
            Task.FromResult(false);

        public Task ShowMessageAsync(string title, string message, string? technicalDetail = null,
            bool isError = false, string? copyPath = null) => Task.CompletedTask;

        public ExportChoice? AskExport(ExportContext context) => null;

        public Task<ScanFinishedAction> ShowScanFinishedAsync(ScanSummary summary) =>
            Task.FromResult(ScanFinishedAction.Close);

        public void ShowAbout(AboutInfo info) { }

        public void ShowHelp() { }

        public void RevealInExplorer(string path) { }

        public void OpenFile(string path) { }

        public bool ShowFirstRun(out bool openFolderPicker)
        {
            openFolderPicker = false;
            return false;
        }
    }

    private sealed class FakeHistory : IScanHistory
    {
        public bool IsOpen => false;

        public string DatabasePath => "память";

        public string? Open(string databasePath) => null;

        public FileHistoryEntry? Find(string path) => null;

        public void Save(FileHistoryEntry entry) { }

        public void Clear() { }

        public void Close() { }

        public int Count() => 0;

        public void Dispose() { }
    }
}
