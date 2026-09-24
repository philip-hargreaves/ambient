using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Appraisal;
using Ambient.App.Core.Shell;

namespace Ambient.App.Features.Appraisal;

/// <summary>The Appraisal page: a journal of reflections, one year at a time.</summary>
public sealed partial class AppraisalsView : UserControl
{
    private bool _syncing;

    public AppraisalsView(AppraisalsViewModel viewModel, ShellViewModel shell)
    {
        ViewModel = viewModel;
        Shell = shell;
        InitializeComponent();
        for (var month = 1; month <= 12; month++)
        {
            MonthBar.Items.Add(new SelectorBarItem { Tag = month });
        }
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppraisalsViewModel.Months))
            {
                SyncMonths();
            }
        };
        SyncMonths();
        Loaded += (_, _) => _ = ViewModel.RefreshAsync();
    }

    public AppraisalsViewModel ViewModel { get; }

    public ShellViewModel Shell { get; }

    // Months with entries can be chosen, this month is in ink, the narrowed month is selected
    private void SyncMonths()
    {
        _syncing = true;
        SelectorBarItem? selected = null;
        for (var i = 0; i < MonthBar.Items.Count && i < ViewModel.Months.Count; i++)
        {
            var marker = ViewModel.Months[i];
            var item = MonthBar.Items[i];
            item.Text = marker.Name;
            item.IsEnabled = marker.Filled;
            item.FontWeight = marker.Current ? FontWeights.SemiBold : FontWeights.Normal;
            ToolTipService.SetToolTip(item, marker.Tip);
            if (marker.Selected)
            {
                selected = item;
            }
        }
        MonthBar.SelectedItem = selected;
        _syncing = false;
    }

    private void OnMonthChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!_syncing && sender.SelectedItem?.Tag is int month && month != ViewModel.MonthFilter)
        {
            ViewModel.ToggleMonth(month);
        }
    }
}
