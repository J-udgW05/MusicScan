using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MusicScanIntegrity.App.ViewModels;

namespace MusicScanIntegrity.App.Views;

/// <summary>Вкладка «Результаты»: таблица, фильтры, подробности выбранного файла.</summary>
public partial class ResultsView
{
    /// <summary>Создаёт представление.</summary>
    public ResultsView() => InitializeComponent();

    /// <summary>
    /// Правый щелчок выделяет строку под курсором.
    /// </summary>
    /// <remarks>
    /// Сам по себе он этого не делает: список отдаёт меню, но выделение
    /// остаётся прежним — и команда открывала бы не тот файл, по которому
    /// щёлкнули, а то и ничего, если выделения не было вовсе. Щелчок внутри
    /// уже выделенного набора выделение не сбрасывает: иначе «Копировать
    /// путь» для нескольких строк было бы недостижимо.
    /// </remarks>
    private void OnRowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Rows, (DependencyObject)e.OriginalSource)
            is not ListBoxItem { IsSelected: false } item)
        {
            return;
        }

        Rows.SelectedItem = item.DataContext;
        item.Focus();
    }

    /// <summary>
    /// Двойной клик по строке открывает папку файла в проводнике —
    /// это самое частое, что хочется сделать с найденной проблемой.
    /// </summary>
    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel main &&
            main.Results.Selected is { } selected &&
            main.RevealResultCommand.CanExecute(selected))
        {
            main.RevealResultCommand.Execute(selected);
        }
    }
}
