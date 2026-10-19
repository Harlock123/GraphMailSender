using System;
using System.Collections.Generic;
using System.IO;

namespace GraphMailSender.Services;

/// <summary>
/// Just enough of a MIME map for the file types people actually attach. Outlook will infer
/// from the file name anyway, so an unknown extension falling back to octet-stream is fine.
/// </summary>
public static class MimeTypes
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".log"] = "text/plain",
        [".csv"] = "text/csv",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".zip"] = "application/zip",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    };

    public static string ForFile(string path) =>
        ByExtension.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
}
