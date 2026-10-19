using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using GraphMailSender.Services;

namespace GraphMailSender.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private static readonly char[] RecipientSeparators = [',', ';', ' ', '\t', '\r', '\n'];

    private readonly GraphMailService _mailService;
    private readonly IFilePickerService? _filePicker;

    private string _tenantId = "";
    private string _clientId = "";
    private string _clientSecret = "";
    private string _senderAddress = "";
    private string _recipients = "";
    private string _ccRecipients = "";
    private string _bccRecipients = "";
    private string _subject = "";
    private string _body = "";
    private bool _isHtml;
    private bool _saveToSentItems = true;
    private bool _isBusy;
    private string _statusMessage = "";
    private bool _statusIsError;

    public MainWindowViewModel() : this(new GraphMailService(), filePicker: null)
    {
    }

    public MainWindowViewModel(GraphMailService mailService, IFilePickerService? filePicker)
    {
        _mailService = mailService;
        _filePicker = filePicker;
        Attachments.CollectionChanged += OnAttachmentsChanged;

        var settings = SettingsStore.Load();
        _tenantId = settings.TenantId;
        _clientId = settings.ClientId;
        _senderAddress = settings.SenderAddress;
        _clientSecret = Environment.GetEnvironmentVariable("GRAPH_CLIENT_SECRET")?.Trim() ?? "";

        SendCommand = new AsyncRelayCommand(SendAsync, () => !IsBusy && IsInputValid);
        AddAttachmentsCommand = new AsyncRelayCommand(AddAttachmentsAsync, () => _filePicker is not null);
    }

    public AsyncRelayCommand SendCommand { get; }

    public AsyncRelayCommand AddAttachmentsCommand { get; }

    public ObservableCollection<AttachmentViewModel> Attachments { get; } = [];

    public string TenantId
    {
        get => _tenantId;
        set => SetAndRevalidate(ref _tenantId, value);
    }

    public string ClientId
    {
        get => _clientId;
        set => SetAndRevalidate(ref _clientId, value);
    }

    public string ClientSecret
    {
        get => _clientSecret;
        set => SetAndRevalidate(ref _clientSecret, value);
    }

    public string SenderAddress
    {
        get => _senderAddress;
        set => SetAndRevalidate(ref _senderAddress, value);
    }

    /// <summary>One or more addresses separated by comma, semicolon, space or newline.</summary>
    public string Recipients
    {
        get => _recipients;
        set => SetAndRevalidate(ref _recipients, value);
    }

    /// <summary>Optional carbon-copy addresses, same separators as <see cref="Recipients"/>.</summary>
    public string CcRecipients
    {
        get => _ccRecipients;
        set => SetAndRevalidate(ref _ccRecipients, value);
    }

    /// <summary>Optional blind-carbon-copy addresses, same separators as <see cref="Recipients"/>.</summary>
    public string BccRecipients
    {
        get => _bccRecipients;
        set => SetAndRevalidate(ref _bccRecipients, value);
    }

    public string Subject
    {
        get => _subject;
        set => SetAndRevalidate(ref _subject, value);
    }

    public string Body
    {
        get => _body;
        set => SetAndRevalidate(ref _body, value);
    }

    public bool IsHtml
    {
        get => _isHtml;
        set => SetProperty(ref _isHtml, value);
    }

    public bool SaveToSentItems
    {
        get => _saveToSentItems;
        set => SetProperty(ref _saveToSentItems, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                SendCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public long TotalAttachmentBytes => Attachments.Sum(attachment => attachment.SizeBytes);

    public bool HasAttachments => Attachments.Count > 0;

    public string AttachmentSummary => Attachments.Count switch
    {
        0 => "No files attached",
        1 => $"1 file, {ByteSize.Format(TotalAttachmentBytes)}",
        var count => $"{count} files, {ByteSize.Format(TotalAttachmentBytes)}",
    };

    public bool AttachmentsTooLarge => TotalAttachmentBytes > GraphMailService.MaxInlineAttachmentBytes;

    public string AttachmentLimitMessage =>
        $"Over the {ByteSize.Format(GraphMailService.MaxInlineAttachmentBytes)} limit for " +
        "attachments sent this way. Remove some files.";

    private bool IsInputValid =>
        !string.IsNullOrWhiteSpace(TenantId)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && IsEmail(SenderAddress)
        && HasAnyRecipient
        && !string.IsNullOrWhiteSpace(Body)
        && !AttachmentsTooLarge;

    /// <summary>Graph only needs one recipient somewhere - To, Cc or Bcc all count.</summary>
    private bool HasAnyRecipient =>
        ParseRecipients(Recipients).Count
        + ParseRecipients(CcRecipients).Count
        + ParseRecipients(BccRecipients).Count > 0;

    private async Task AddAttachmentsAsync()
    {
        if (_filePicker is null)
        {
            return;
        }

        var paths = await _filePicker.PickFilesAsync();
        foreach (var path in paths)
        {
            // Picking the same file twice would attach it twice.
            if (Attachments.Any(a => string.Equals(a.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Attachments.Add(new AttachmentViewModel(path, item => Attachments.Remove(item)));
        }
    }

    private void OnAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TotalAttachmentBytes));
        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(AttachmentSummary));
        OnPropertyChanged(nameof(AttachmentsTooLarge));
        SendCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Reads each pending attachment off disk. Files can move between picking and sending.</summary>
    private bool TryReadAttachments(out List<MailAttachment> attachments)
    {
        attachments = [];

        foreach (var item in Attachments)
        {
            try
            {
                attachments.Add(new MailAttachment(
                    item.Name,
                    MimeTypes.ForFile(item.FilePath),
                    File.ReadAllBytes(item.FilePath)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus($"Could not read {item.Name}: {ex.Message}", isError: true);
                return false;
            }
        }

        return true;
    }

    private async Task SendAsync()
    {
        IsBusy = true;
        SetStatus("Sending...", isError: false);

        try
        {
            var to = ParseRecipients(Recipients);
            var cc = ParseRecipients(CcRecipients);
            var bcc = ParseRecipients(BccRecipients);

            var invalid = to.Concat(cc).Concat(bcc)
                .Where(address => !IsEmail(address))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (invalid.Count > 0)
            {
                SetStatus($"Not a valid email address: {string.Join(", ", invalid)}", isError: true);
                return;
            }

            var credentials = new GraphCredentials(
                TenantId.Trim(),
                ClientId.Trim(),
                ClientSecret,
                SenderAddress.Trim());

            if (!TryReadAttachments(out var attachments))
            {
                return;
            }

            var mail = new MailRequest(
                to, cc, bcc, Subject, Body, IsHtml, SaveToSentItems, attachments);

            await _mailService.SendAsync(credentials, mail);

            SettingsStore.Save(new AppSettings
            {
                TenantId = credentials.TenantId,
                ClientId = credentials.ClientId,
                SenderAddress = credentials.SenderAddress,
            });

            SetStatus(DescribeSent(to, cc, bcc), isError: false);
        }
        catch (GraphMailException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            SetStatus($"Unexpected error: {ex.Message}", isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
    }

    private void SetAndRevalidate<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            SendCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>"Sent to a@b.com." with a trailing "(1 cc, 2 bcc)" only when those were used.</summary>
    private static string DescribeSent(
        IReadOnlyList<string> to,
        IReadOnlyList<string> cc,
        IReadOnlyList<string> bcc)
    {
        var extras = new List<string>();
        if (cc.Count > 0)
        {
            extras.Add($"{cc.Count} cc");
        }

        if (bcc.Count > 0)
        {
            extras.Add($"{bcc.Count} bcc");
        }

        // A Bcc-only send has nothing to name, so don't claim "Sent to ."
        var sent = to.Count > 0 ? $"Sent to {string.Join(", ", to)}." : "Sent.";
        return extras.Count > 0 ? $"{sent} ({string.Join(", ", extras)})" : sent;
    }

    private static List<string> ParseRecipients(string raw) =>
        raw.Split(RecipientSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool IsEmail(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && MailAddress.TryCreate(value.Trim(), out var parsed)
        && parsed.Address.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase);
}
