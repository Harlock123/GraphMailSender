using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GraphMailSender.Services;

namespace GraphMailSender.Views;

/// <summary>Avalonia-backed <see cref="IFilePickerService"/> hung off the owning window.</summary>
public sealed class StorageProviderFilePicker(Visual owner) : IFilePickerService
{
    public async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var storage = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (storage is null)
        {
            return [];
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Attach files",
            AllowMultiple = true,
        });

        // Anything without a local path (a cloud placeholder, say) can't be read off disk.
        return files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList();
    }
}
