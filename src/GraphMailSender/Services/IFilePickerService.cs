using System.Collections.Generic;
using System.Threading.Tasks;

namespace GraphMailSender.Services;

/// <summary>
/// Lets the view model ask for files without taking a dependency on Avalonia's storage
/// provider - and lets it run headless, where there is no window to hang a dialog off.
/// </summary>
public interface IFilePickerService
{
    /// <summary>Returns the chosen local paths, or an empty list if the user cancelled.</summary>
    Task<IReadOnlyList<string>> PickFilesAsync();
}
