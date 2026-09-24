using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Documents;

namespace Ambient.App.Features.Documents;

/// <summary>
/// The consultation's right pane: the two editors behind a selector. The editors are
/// their own controls so the Sessions view shows the same ones.
/// </summary>
public sealed partial class NotePaneView : UserControl
{
    public NotePaneView(NoteViewModel viewModel, NoteEditorView note, PatientEditorView patient)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Tabs.Add("Clinical note", note);
        Tabs.Add("Patient information", patient);
        Tabs.Loaded += (_, _) => Fit();
        Tabs.SizeChanged += (_, _) => Fit();
        void Fit()
        {
            note.FitTabContent(Tabs.ContentArea);
            patient.FitTabContent(Tabs.ContentArea);
        }
    }

    public NoteViewModel ViewModel { get; }
}
