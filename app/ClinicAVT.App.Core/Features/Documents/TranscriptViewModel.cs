using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ClinicAVT.App.Core.Common;

namespace ClinicAVT.App.Core.Features.Documents;

public sealed partial class TranscriptViewModel : ObservableObject
{
    private const int SampleRate = 16000;

    public ObservableCollection<TranscriptTurnItem> Turns { get; } = [];

    public void Add(string speaker, ulong firstFrame, string text) =>
        Turns.Add(new TranscriptTurnItem(speaker, Words.Position(firstFrame / (double)SampleRate), text));

    public void Clear() => Turns.Clear();
}
