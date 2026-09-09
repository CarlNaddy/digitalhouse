namespace DigitalHouse.Localization;

/// <summary>
/// Marker type for the app-wide shared resource file. Inject
/// <c>IStringLocalizer&lt;SharedResource&gt;</c> and look strings up by key;
/// translations live in <c>Resources/Localization/SharedResource.&lt;culture&gt;.resx</c>.
/// </summary>
public sealed class SharedResource;
