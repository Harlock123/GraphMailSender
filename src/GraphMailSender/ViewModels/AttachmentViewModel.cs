using System;
using System.IO;
using System.Windows.Input;
using GraphMailSender.Services;

namespace GraphMailSender.ViewModels;

/// <summary>
/// One pending attachment. The file is only read at send time - this just remembers where
/// it is and how big it was when picked.
/// </summary>
public sealed class AttachmentViewModel
{
    public AttachmentViewModel(string filePath, Action<AttachmentViewModel> remove)
    {
        FilePath = filePath;
        Name = Path.GetFileName(filePath);

        try
        {
            SizeBytes = new FileInfo(filePath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SizeBytes = 0;
        }

        RemoveCommand = new RelayCommand(_ => remove(this));
    }

    public string FilePath { get; }

    public string Name { get; }

    public long SizeBytes { get; }

    public string SizeDisplay => ByteSize.Format(SizeBytes);

    public ICommand RemoveCommand { get; }
}
