using System;
using System.IO;
using System.Text.Json;

namespace GraphMailSender.Services;

/// <summary>
/// The non-secret half of the app registration details, remembered between runs so you
/// only type them once. The client secret is never written to disk - supply it per run,
/// or via the GRAPH_CLIENT_SECRET environment variable.
/// </summary>
public sealed class AppSettings
{
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string SenderAddress { get; set; } = "";
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GraphMailSender",
        "settings.json");

    /// <summary>Loads saved settings, then lets GRAPH_* environment variables win.</summary>
    public static AppSettings Load()
    {
        var settings = new AppSettings();

        try
        {
            if (File.Exists(SettingsPath))
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? settings;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings shouldn't stop the app starting.
        }

        settings.TenantId = Override(settings.TenantId, "GRAPH_TENANT_ID");
        settings.ClientId = Override(settings.ClientId, "GRAPH_CLIENT_ID");
        settings.SenderAddress = Override(settings.SenderAddress, "GRAPH_SENDER");
        return settings;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember settings is not worth failing a send over.
        }
    }

    private static string Override(string current, string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? current : value.Trim();
    }
}
