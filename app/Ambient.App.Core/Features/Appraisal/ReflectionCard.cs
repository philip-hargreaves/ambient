using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ambient.App.Core.Common;

namespace Ambient.App.Core.Features.Appraisal;

/// <summary>One consultation the clinician reflected on: a journal card.</summary>
public sealed partial class ReflectionCard : ObservableObject
{
    public ReflectionCard(string id, string title, DateTimeOffset started, string learned,
        string summary = "", bool demo = false, string happened = "", string next = "")
    {
        Id = id;
        Title = title;
        Started = started;
        Learned = learned;
        Summary = summary;
        Demo = demo;
        Happened = happened;
        Next = next;
    }

    public string Id { get; }

    /// <summary>A seeded sample.</summary>
    public bool Demo { get; }

    public DateTimeOffset Started { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>Searched, not shown.</summary>
    [ObservableProperty]
    public partial string Happened { get; set; }

    /// <summary>Searched, not shown.</summary>
    [ObservableProperty]
    public partial string Next { get; set; }

    /// <summary>Searched, and shown until there is a case study.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Line))]
    public partial string Learned { get; set; }

    /// <summary>The case study, shown on the closed card.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Line))]
    public partial string Summary { get; set; }

    public string Line => (Summary.Trim().Length > 0 ? Summary : Learned).Trim();

    public string MonthLabel => Words.Month(Started);

    /// <summary>"September": the group heading, under a year stepper.</summary>
    public string MonthHeading => Started.ToString("MMMM", CultureInfo.CurrentCulture);

    /// <summary>"14 Sep": the day on the closed card.</summary>
    public string DayLabel => Started.ToString("d MMM", CultureInfo.CurrentCulture);

    /// <summary>True on the first card of each month, so the month heads the group.</summary>
    [ObservableProperty]
    public partial bool StartsMonth { get; set; }

    [ObservableProperty]
    public partial bool Expanded { get; set; }

    /// <summary>The editor, present only while the card is open.</summary>
    [ObservableProperty]
    public partial ReflectionViewModel? Editor { get; set; }

    /// <summary>Set by the page, which owns the engine.</summary>
    public Func<ReflectionCard, Task>? ToggleRequested { get; set; }

    public Func<ReflectionCard, Task>? DeleteRequested { get; set; }

    /// <summary>Everything a search may match: title, case study and the three answers.</summary>
    public bool Matches(string needle) =>
        Title.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
        || Summary.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
        || Happened.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
        || Learned.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
        || Next.Contains(needle, StringComparison.CurrentCultureIgnoreCase);

    [RelayCommand]
    private Task Toggle() => ToggleRequested?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand]
    private Task Delete() => DeleteRequested?.Invoke(this) ?? Task.CompletedTask;
}
