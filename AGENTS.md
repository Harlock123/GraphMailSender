# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers the tenant setup, the inputs, the
attachment limit and troubleshooting; this file covers how the code fits together and how to change it safely.

## What this is

A small Avalonia 12 desktop app on .NET 10 that sends one email through Microsoft Graph's
`/users/{sender}/sendMail`. It uses app-only (client credentials) authentication, so there is no interactive
sign-in. It runs on Windows, macOS and Linux.

## Commands

```bash
dotnet build GraphMailSender.slnx                       # no global.json: any .NET 10 SDK
dotnet build GraphMailSender.slnx -c Release -warnaserror   # what CI runs
dotnet run --project src/GraphMailSender                # opens the window
```

The window's fields can be prefilled from `GRAPH_TENANT_ID`, `GRAPH_CLIENT_ID`, `GRAPH_CLIENT_SECRET` and
`GRAPH_SENDER`, which override the saved settings. There are no tests yet.

## Layout

- `src/GraphMailSender/Services/`: everything that isn't the window.
  - `GraphMailService.cs` gets the token (`Azure.Identity` `ClientSecretCredential`) and posts the message with plain
    `HttpClient` and JSON. It is the only file that knows about Graph.
  - `SettingsStore.cs` remembers the non-secret settings.
  - `IFilePickerService.cs` keeps Avalonia's storage API out of the view model.
  - `MimeTypes.cs` and `ByteSize.cs` are small helpers.
- `src/GraphMailSender/ViewModels/`: `MainWindowViewModel` holds the input state, validation and the send. The MVVM
  helpers (`ViewModelBase`, `RelayCommand`, `AsyncRelayCommand`) are hand-written, with no MVVM library.
- `src/GraphMailSender/Views/`: the single `MainWindow` and `StorageProviderFilePicker`.
- `docs/`: the README's screenshots.

## Rules that keep it working

- **The client secret never reaches disk.** `SettingsStore` saves only the tenant ID, client ID and sender, to
  `GraphMailSender/settings.json` under `SpecialFolder.ApplicationData`, and only after a successful send. Keep any
  new secret out of it.
- **Attachments stop at 3 MB in total** (`GraphMailService.MaxInlineAttachmentBytes`). Graph caps the request at
  4 MB, and base64 adds a third. Going past it needs an upload session, which this app doesn't implement.
- **Show errors in full.** The service unwraps the inner Entra exception to show the `AADSTS` code, and Graph's
  JSON error envelope to show its `code` and `message`. Keep that when you change error handling.
- **Compiled bindings.** `AvaloniaUseCompiledBindingsByDefault` is on, so a binding typo is a build error.
- **No warnings.** The project doesn't set `TreatWarningsAsErrors`, but CI builds with `-warnaserror`, so a warning
  fails CI.
- **Line endings.** The sources are LF; keep them that way.

## Making a change

- Build with `-warnaserror` before pushing, and try a real send against a test tenant if you touch the service.
- Update `README.md` (inputs, limits, troubleshooting, the project layout) and its screenshots to match.

## CI and releases

`.github/workflows/build.yml` restores and builds in Release with `-warnaserror` on Ubuntu 24.04, Windows and
macOS. It runs on pushes and pull requests to `main`, and by hand. It publishes nothing and makes no releases.
