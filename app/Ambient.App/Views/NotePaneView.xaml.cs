using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.ViewModels;

namespace Ambient.App.Views;

/// <summary>
/// The consultation's right pane: the two editors in tabs. The editors are
/// their own controls so the Sessions view shows the same ones.
/// </summary>
public sealed partial class NotePaneView : UserControl
{
    public NotePaneView(NoteViewModel viewModel, NoteEditorView note, PatientEditorView patient)
    {
        ViewModel = viewModel;
        InitializeComponent();
        NoteHost.Content = note;
        PatientHost.Content = patient;
        Tabs.Loaded += (_, _) => Fit();
        Tabs.SizeChanged += (_, _) => Fit();
        void Fit()
        {
            note.FitTabContent(Tabs);
            patient.FitTabContent(Tabs);
        }
    }

    public NoteViewModel ViewModel { get; }
}
