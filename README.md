# Graph Mail Sender

[![build](https://github.com/Harlock123/GraphMailSender/actions/workflows/build.yml/badge.svg)](https://github.com/Harlock123/GraphMailSender/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-12.1-8B44AC)](https://avaloniaui.net/)
[![Microsoft Graph](https://img.shields.io/badge/Microsoft%20Graph-v1.0-0078D4)](https://learn.microsoft.com/en-us/graph/api/user-sendmail)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](https://avaloniaui.net/)

A small Avalonia desktop app (.NET 10) that sends a single email through the Microsoft Graph
API. You type the app-registration details, a recipient, a subject and a message, and it
`POST`s to `/users/{sender}/sendMail`.

Authentication is **app-only (client credentials)** — no interactive sign-in.

## Build & run

```powershell
dotnet run --project src\GraphMailSender
```

Requires the .NET 10 SDK.

## Azure setup (one time)

1. **Entra admin center → App registrations → New registration.** Any name; no redirect URI
   is needed for client-credentials flow.
2. Note the **Application (client) ID** and **Directory (tenant) ID** from the Overview page.
3. **Certificates & secrets → New client secret.** Copy the secret *value* immediately.
4. **API permissions → Add a permission → Microsoft Graph → Application permissions →
   `Mail.Send`**, then **Grant admin consent**. Delegated `Mail.Send` will not work here.
5. Pick the mailbox to send from. `Mail.Send` as an application permission grants access to
   *every* mailbox in the tenant, so in production scope it down with an
   [application access policy](https://learn.microsoft.com/en-us/graph/auth-limit-mailbox-access).

## Fields

| Field | What it is |
| --- | --- |
| Tenant ID | Directory (tenant) ID, or a domain like `contoso.onmicrosoft.com` |
| Client ID | Application (client) ID of the registration |
| Client secret | The secret *value* from Certificates & secrets |
| Send as | UPN or object ID of the mailbox the mail is sent from |
| To | One or more addresses, separated by `,` `;` or whitespace |
| Subject / Message | Message content; tick **HTML body** to send the text as HTML |
| Save to Sent Items | Whether Graph keeps a copy in the sender's Sent Items |

## Settings and secrets

Tenant ID, client ID and sender are remembered in
`%APPDATA%\GraphMailSender\settings.json` after a successful send. **The client secret is
never written to disk.**

All four can be prefilled from the environment, which overrides the saved file:

```powershell
$env:GRAPH_TENANT_ID     = "..."
$env:GRAPH_CLIENT_ID     = "..."
$env:GRAPH_CLIENT_SECRET = "..."
$env:GRAPH_SENDER        = "noreply@contoso.com"
dotnet run --project src\GraphMailSender
```

## Layout

```
src/GraphMailSender/
  Program.cs                        Avalonia entry point
  App.axaml(.cs)                    Application + Fluent theme
  Views/MainWindow.axaml(.cs)       The single window
  ViewModels/MainWindowViewModel.cs Input state, validation, send orchestration
  ViewModels/ViewModelBase.cs       INotifyPropertyChanged helper
  ViewModels/AsyncRelayCommand.cs   Minimal async ICommand
  Services/GraphMailService.cs      Token acquisition + sendMail call
  Services/SettingsStore.cs         Non-secret settings persistence
```

`GraphMailService` uses `Azure.Identity` for the token and plain `HttpClient` + JSON for the
Graph call, which keeps the dependency footprint to one package. If you later want typed Graph
models, swap it for the `Microsoft.Graph` SDK — the service is the only file that would change.

## Troubleshooting

| Message | Likely cause |
| --- | --- |
| `Sign-in failed: AADSTS7000215` | Wrong client secret |
| `Sign-in failed: AADSTS700016` | Wrong client ID, or app not in that tenant |
| `Graph returned 403: ErrorAccessDenied` | `Mail.Send` not granted as an *application* permission, or admin consent missing |
| `Graph returned 404: ResourceNotFound` | The **Send as** mailbox doesn't exist (or has no Exchange Online mailbox) |

## License

[MIT](LICENSE) — © 2026 Lonnie Watson
