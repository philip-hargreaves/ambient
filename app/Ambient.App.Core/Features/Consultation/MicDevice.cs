namespace Ambient.App.Core.Features.Consultation;

/// <summary>A capture endpoint as the engine listed it. The id is WASAPI's.</summary>
public sealed record MicDevice(string Id, string Name, string ShortName, bool IsDefault, bool Bluetooth);
