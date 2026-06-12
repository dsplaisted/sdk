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

    // Index into the "Modify shell profile" field's choices. Yes is the recommended default.
    private const int ProfileYesIndex = 0;

    // Index into the Migrate field's choices.
    private const int MigrateYesIndex = 0;

    private readonly FormField _profileField;
    private readonly FormField _migrateField;

    private InitFormModel(
        string subtitle,
        string question,
        IReadOnlyList<FormField> fields,
        FormField profileField,
        FormField migrateField,
        string installPath,
        string profilePath,
        IReadOnlyList<string> sdkVersions,
        IReadOnlyList<string> runtimeVersions)
    {
        Subtitle = subtitle;
        Question = question;
        Fields = fields;
        _profileField = profileField;
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

    /// <summary>Mock shell profile file that the "Modify shell profile" option would edit.</summary>
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

        if (ReferenceEquals(field, _profileField))
        {
            lines.Add(new DetailLine("Installs to:", InstallPath));
            if (choiceIndex == ProfileYesIndex)
            {
                lines.Add(new DetailLine("Edits profile:", ProfilePath));
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
    /// Creates the prototype's sample form: SDK Channel, two profile/PATH yes/no questions, and
    /// Migrate. Mock values + help text.
    /// </summary>
    public static InitFormModel CreateSample()
    {
        var channel = new FormField(
            "SDK Channel",
            [
                new FieldChoice("latest", "The latest released SDK"),
                new FieldChoice("preview", "The latest preview SDK"),
                new FieldChoice("daily", "The latest daily build"),
                new FieldChoice("10.0", "The latest 10.0 SDK"),
                new FieldChoice("10.0.1xx", "The latest 10.0.1xx patch"),
                new FieldChoice("9.0", "The latest 9.0 SDK"),
                new FieldChoice("<other>", "Type your own, e.g. 8.0.4xx", IsCustomInput: true),
            ],
            defaultIndex: 0,
            inlineHelp: true);

        // Prompt 1 (always shown): configure the shell profile? Yes is recommended.
        var profile = new FormField(
            "Modify shell profile",
            [
                new FieldChoice("Yes", "Modify the current shell profile so `dotnet` resolves to dotnetup's installs in new terminals. Recommended if you develop with dotnetup and launch your IDE from the terminal. Only PowerShell is supported; CMD has no profile file."),
                new FieldChoice("No", "Use `dotnetup dotnet` to run installs managed by dotnetup alongside your existing installs. Recommended if you don't have admin rights, or want to keep your system-managed installs (e.g. Program Files) as the default."),
            ],
            defaultIndex: ProfileYesIndex);

        // Prompt 2 (Windows; shown only when the profile is being modified and a system .NET install
        // exists): replace the system-level PATH entry? No is recommended.
        var path = new FormField(
            "Replace system PATH",
            [
                new FieldChoice("No", "Your existing system installs are still used outside the shell(s) you configured."),
                new FieldChoice("Yes", "Make every debugger, application, and IDE use dotnetup's installs by default. Requires admin rights and enables `cmd` by default. Other user accounts on this machine are affected and applications may break."),
            ],
            defaultIndex: 0,
            isVisible: () => profile.SelectedIndex == ProfileYesIndex);

        var migrate = new FormField(
            "Migrate system installs",
            [
                new FieldChoice("Yes", "Copy .NET SDKs and runtimes from your existing system-wide install under dotnetup's management so they're updated and cleaned up together."),
                new FieldChoice("No", "Leave existing system-wide .NET installs untouched. dotnetup manages only what it installs."),
            ],
            defaultIndex: 0);

        return new InitFormModel(
            subtitle: "Welcome to dotnetup!",
            question: "Install .NET with these settings?",
            fields: [channel, profile, path, migrate],
            profileField: profile,
            migrateField: migrate,
            installPath: Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\.dotnet"),
            profilePath: Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Documents\PowerShell\Microsoft.PowerShell_profile.ps1"),
            sdkVersions: ["10.0.105", "9.0.403", "9.0.112", "8.0.324", "8.0.118", "7.0.410", "6.0.428"],
            runtimeVersions: ["10.0.5", "9.0.12", "8.0.20", "8.0.18", "7.0.20", "6.0.36"]);
    }
}
