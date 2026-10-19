namespace GraphMailSender.Services;

/// <summary>Short human-readable file sizes, shared by the attachment list and the size guard.</summary>
public static class ByteSize
{
    public static string Format(long bytes) => bytes switch
    {
        >= 1024L * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        1 => "1 byte",
        _ => $"{bytes} bytes",
    };
}
