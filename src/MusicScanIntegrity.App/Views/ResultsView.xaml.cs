using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MusicScanIntegrity.App.ViewModels;

namespace MusicScanIntegrity.App.Views;

/// <summary>Results tab: table, filters and details of the selected file.</summary>
public partial class ResultsView
{
    public ResultsView() => InitializeComponent();

    /// <summary>Right-click selects the row under the cursor.</summary>
    /// <remarks>
    /// ListBox does not do this itself: the menu opens but the selection stays,
    /// so commands would act on the wrong file or on nothing. A click inside an
    /// existing multi-selection keeps it, so "Copy path" still works for several
    /// rows.
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

    /// <summary>Double-click reveals the file in Explorer.</summary>
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
