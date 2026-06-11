namespace ControlMenu.Modules.Imaging.Pages;

/// <summary>
/// JS-interop DTO returned by the <c>filePickerOpen</c> helper (wwwroot/js/file-picker.js):
/// the selected file's name and its bytes as base64. Shared by the Imaging pages that read a
/// file via the File System Access API (currently Magic Wand). Public so render tests can hand
/// the same type to the bUnit JSInterop mock — JSInterop matches the invocation's result type,
/// so the page and the test must reference one shared type.
/// </summary>
public record FilePickerResult(string Name, string BytesBase64);

/// <summary>
/// JS-interop DTO returned by the <c>getElementRect</c> helper: an image element's intrinsic
/// (natural) and rendered (client) dimensions, used to map a click's rendered coordinates back
/// to source-pixel coordinates for Magic Wand click-to-seed.
/// </summary>
public record ElementRect(double NaturalWidth, double NaturalHeight, double RenderedWidth, double RenderedHeight);
