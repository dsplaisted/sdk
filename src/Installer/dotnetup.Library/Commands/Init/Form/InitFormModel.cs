// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.Tools.Bootstrapper.Commands.Init.Form;

/// <summary>
/// Builds the mock form shown by the <c>initform</c> prototype. This is UI-only sample data: the
/// fields, values, and help text are static and not resolved from real default/install logic.
/// </summary>
internal sealed class InitFormModel
{
    public InitFormModel(string subtitle, string question, IReadOnlyList<FormField> fields)
    {
        Subtitle = subtitle;
        Question = question;
        Fields = fields;
    }

    /// <summary>Short line under the banner (e.g. a welcome message).</summary>
    public string Subtitle { get; }

    /// <summary>The prompt shown above the fields (e.g. "Install .NET with these settings?").</summary>
    public string Question { get; }

    /// <summary>The form fields, in display order.</summary>
    public IReadOnlyList<FormField> Fields { get; }

    /// <summary>
    /// Creates the prototype's sample form: SDK Channel, Mode, and Migrate. Mock values + help text.
    /// </summary>
    public static InitFormModel CreateSample()
    {
        var channel = new FormField(
            "SDK Channel",
            [
                new FieldChoice("10.0 (from global.json)", "Use the .NET 10.0 SDK band implied by the global.json found in this directory. Recommended so your builds match the repository's pinned version."),
                new FieldChoice("latest", "Always install the newest released .NET SDK. Good for trying the latest features; may differ from what a repository expects."),
                new FieldChoice("10.0.1xx", "Pin to the 10.0.1xx feature band and take the newest patch within it. Stable for day-to-day development on .NET 10."),
                new FieldChoice("9.0", "Install the latest .NET 9.0 SDK. Use when you need to target or maintain .NET 9 projects."),
                new FieldChoice("preview / daily", "Install the newest preview or daily build. For early testing of in-development features; not for production."),
            ],
            defaultIndex: 0);

        var mode = new FormField(
            "Mode",
            [
                new FieldChoice("Terminal Profile (recommended)", "Adds dotnetup's .NET to your shell profile's PATH so `dotnet` resolves to it in new terminals. A good balance of convenience and isolation; doesn't touch system-wide settings."),
                new FieldChoice("Isolation", "Keep dotnetup's .NET fully self-contained; nothing is added to PATH. You invoke it explicitly. Safest option if you manage multiple .NET installs yourself."),
                new FieldChoice("Replacement", "Make dotnetup's .NET the machine's primary `dotnet`. Most seamless, but overrides any existing system-wide .NET on PATH."),
            ],
            defaultIndex: 0);

        var migrate = new FormField(
            "Migrate system installs",
            [
                new FieldChoice("Yes", "Move .NET SDKs and runtimes from your existing system-wide install under dotnetup's management so they're updated and cleaned up together."),
                new FieldChoice("No", "Leave existing system-wide .NET installs untouched. dotnetup manages only what it installs."),
            ],
            defaultIndex: 0);

        return new InitFormModel(
            subtitle: "Welcome to dotnetup!",
            question: "Install .NET with these settings?",
            fields: [channel, mode, migrate]);
    }
}
