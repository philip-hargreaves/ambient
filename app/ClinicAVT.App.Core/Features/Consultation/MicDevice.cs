namespace ClinicAVT.App.Core.Features.Consultation;

/// <summary>A capture endpoint as the engine lists it. Id is the WASAPI endpoint id.</summary>
public sealed record MicDevice(string Id, string Name, string ShortName, bool IsDefault, bool Bluetooth);
