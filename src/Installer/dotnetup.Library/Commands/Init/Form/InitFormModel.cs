// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Linq;

namespace Microsoft.DotNet.Tools.Bootstrapper.Commands.Init.Form;

/// <summary>
/// Builds the mock form shown by the <c>initform</c> prototype and computes the per-field detail
/// shown alongside it. This is UI-only sample data: the fields, values, paths, and system-install
/// list are static and not resolved from real default/install logic. The detail/summary methods are
/// pure (no console) so they can be unit-tested.
/// </summary>
internal sealed class InitFormModel
{
    // Maximum number of versions listed per component line before collapsing to "and N more".
    private const int MaxVersionsShown = 3;

    // Indices into the Mode field's choices (see CreateSample for construction order).
    private const int ModeTerminalProfileIndex = 1;
    private const int ModeReplacementIndex = 2;

    // Index into the Migrate field's choices.
    private const int MigrateYesIndex = 0;

    private readonly FormField _modeField;
    private readonly FormField _migrateField;

    private InitFormModel(
        string subtitle,
        string question,
        IReadOnlyList<FormField> fields,
        FormField modeField,
        FormField migrateField,
        string installPath,
        string profilePath,
        IReadOnlyList<string> sdkVersions,
        IReadOnlyList<string> runtimeVersions)
    {
        Subtitle = subtitle;
        Question = question;
        Fields = fields;
        _modeField = modeField;
        _migrateField = migrateField;
        InstallPath = installPath;
        ProfilePath = profilePath;
        SdkVersions = sdkVersions;
        RuntimeVersions = runtimeVersions;
    }

    /// <summary>Short line under the banner (e.g. a welcome message).</summary>
    public string Subtitle { get; }

    /// <summary>The prompt shown above the fields.</summary>
    public string Question { get; }

    /// <summary>The form fields, in display order.</summary>
    public IReadOnlyList<FormField> Fields { get; }

    /// <summary>Mock location where .NET would be installed.</summary>
    public string InstallPath { get; }

    /// <summary>Mock shell profile file that Terminal Profile mode would edit.</summary>
    public string ProfilePath { get; }

    /// <summary>Mock SDK versions from the existing system install that could be migrated.</summary>
    public IReadOnlyList<string> SdkVersions { get; }

    /// <summary>Mock runtime versions from the existing system install that could be migrated.</summary>
    public IReadOnlyList<string> RuntimeVersions { get; }

    /// <summary>
    /// Computes the detail (help text + derived info lines) for the given field's value at
    /// <paramref name="choiceIndex"/>. Used both while browsing (the selected value) and while
    /// editing (the highlighted value).
    /// </summary>
    public FieldDetail BuildDetail(FormField field, int choiceIndex)
    {
        string helper = field.Choices[choiceIndex].HelperText;
        var lines = new List<DetailLine>();

        if (ReferenceEquals(field, _modeField))
        {
            lines.Add(new DetailLine("Installs to:", InstallPath));
            if (choiceIndex == ModeTerminalProfileIndex)
            {
                lines.Add(new DetailLine("Edits profile:", ProfilePath));
            }
            else if (choiceIndex == ModeReplacementIndex)
            {
                lines.Add(new DetailLine("Replaces dotnet on the system PATH"));
            }
        }
        else if (ReferenceEquals(field, _migrateField) && choiceIndex == MigrateYesIndex)
        {
            lines.AddRange(BuildMigrationLines());
        }

        return new FieldDetail(helper, lines);
    }

    private List<DetailLine> BuildMigrationLines()
    {
        return
        [
            new DetailLine("SDKs:", FormatVersions(SdkVersions)),
            new DetailLine("Runtimes:", FormatVersions(RuntimeVersions)),
        ];
    }

    // Joins the first few versions and collapses the rest into "and N more".
    private static string FormatVersions(IReadOnlyList<string> versions)
    {
        int shown = Math.Min(MaxVersionsShown, versions.Count);
        string joined = string.Join(", ", versions.Take(shown));

        int remaining = versions.Count - shown;
        return remaining > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}, and {1} more", joined, remaining)
            : joined;
    }

    /// <summary>
    /// Creates the prototype's sample form: SDK Channel, Mode, and Migrate. Mock values + help text.
    /// </summary>
    public static InitFormModel CreateSample()
    {
        var channel = new FormField(
            "SDK Channel",
            [
                new FieldChoice("latest", "Newest released SDK"),
                new FieldChoice("10.0", "Latest .NET 10.0 SDK"),
                new FieldChoice("10.0.1xx", "Newest patch of the 10.0.1xx band"),
                new FieldChoice("9.0", "Latest .NET 9.0 SDK"),
                new FieldChoice("preview / daily", "Newest in-development build"),
                new FieldChoice("custom…", "Type your own, e.g. 8.0.4xx", IsCustomInput: true),
            ],
            defaultIndex: 0,
            inlineHelp: true);

        // Order: Isolation, Terminal Profile (recommended/default), Replacement.
        var mode = new FormField(
            "Mode",
            [
                new FieldChoice("Isolation", "Keep dotnetup's .NET fully self-contained; nothing is added to PATH. You invoke it explicitly. Safest option if you manage multiple .NET installs yourself."),
                new FieldChoice("Terminal Profile (recommended)", "Adds dotnetup's .NET to your shell profile's PATH so `dotnet` resolves to it in new terminals. A good balance of convenience and isolation; doesn't touch system-wide settings."),
                new FieldChoice("Replacement", "Make dotnetup's .NET the machine's primary `dotnet`. Most seamless, but overrides any existing system-wide .NET on PATH."),
            ],
            defaultIndex: ModeTerminalProfileIndex);

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
            fields: [channel, mode, migrate],
            modeField: mode,
            migrateField: migrate,
            installPath: "~/.dotnet",
            profilePath: "~/.bashrc",
            sdkVersions: ["10.0.105", "9.0.403", "9.0.112", "8.0.324", "8.0.118", "7.0.410", "6.0.428"],
            runtimeVersions: ["10.0.5", "9.0.12", "8.0.20", "8.0.18", "7.0.20", "6.0.36"]);
    }
}
