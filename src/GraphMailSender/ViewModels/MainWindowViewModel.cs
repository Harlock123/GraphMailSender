using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using GraphMailSender.Services;

namespace GraphMailSender.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private static readonly char[] RecipientSeparators = [',', ';', ' ', '\t', '\r', '\n'];

    private readonly GraphMailService _mailService;

    private string _tenantId = "";
    private string _clientId = "";
    private string _clientSecret = "";
    private string _senderAddress = "";
    private string _recipients = "";
    private string _subject = "";
    private string _body = "";
    private bool _isHtml;
    private bool _saveToSentItems = true;
    private bool _isBusy;
    private string _statusMessage = "";
    private bool _statusIsError;

    public MainWindowViewModel() : this(new GraphMailService())
    {
    }

    public MainWindowViewModel(GraphMailService mailService)
    {
        _mailService = mailService;

        var settings = SettingsStore.Load();
        _tenantId = settings.TenantId;
        _clientId = settings.ClientId;
        _senderAddress = settings.SenderAddress;
        _clientSecret = Environment.GetEnvironmentVariable("GRAPH_CLIENT_SECRET")?.Trim() ?? "";

        SendCommand = new AsyncRelayCommand(SendAsync, () => !IsBusy && IsInputValid);
    }

    public AsyncRelayCommand SendCommand { get; }

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

    private bool IsInputValid =>
        !string.IsNullOrWhiteSpace(TenantId)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && IsEmail(SenderAddress)
        && ParseRecipients(Recipients).Count > 0
        && !string.IsNullOrWhiteSpace(Body);

    private async Task SendAsync()
    {
        IsBusy = true;
        SetStatus("Sending...", isError: false);

        try
        {
            var recipients = ParseRecipients(Recipients);
            var invalid = recipients.Where(address => !IsEmail(address)).ToList();
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

            var mail = new MailRequest(recipients, Subject, Body, IsHtml, SaveToSentItems);

            await _mailService.SendAsync(credentials, mail);

            SettingsStore.Save(new AppSettings
            {
                TenantId = credentials.TenantId,
                ClientId = credentials.ClientId,
                SenderAddress = credentials.SenderAddress,
            });

            SetStatus($"Sent to {string.Join(", ", recipients)}.", isError: false);
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

    private static List<string> ParseRecipients(string raw) =>
        raw.Split(RecipientSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool IsEmail(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && MailAddress.TryCreate(value.Trim(), out var parsed)
        && parsed.Address.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase);
}
