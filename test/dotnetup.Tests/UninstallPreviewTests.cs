// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;
using Microsoft.Dotnet.Installation;
using Microsoft.DotNet.Tools.Bootstrapper;
using Microsoft.DotNet.Tools.Bootstrapper.Commands.Shared;
using Microsoft.DotNet.Tools.Dotnetup.Tests.Utilities;
using Spectre.Console;

namespace Microsoft.DotNet.Tools.Dotnetup.Tests;

[TestClass]
public class UninstallPreviewTests
{
    [TestMethod]
    [ResourceLock(WellKnownResources.Console)]
    [DataRow(false)]
    [DataRow(true)]
    public void RequiredSpecs_UseListColumnsSourcesAndSingularOrPluralWarning(bool multiple)
    {
        using var fixture = new UninstallTestEnvironment();
        fixture.AddInstallation(InstallComponent.SDK, "8.0.100");
        fixture.AddInstallation(InstallComponent.Runtime, "9.0.0");
        fixture.AddSpecs(InstallComponent.SDK, "8");
        if (multiple)
        {
            fixture.AddSpecs(InstallComponent.SDK, "8.0");
        }
        var root = fixture.ReadRoot();

        var output = Render(() => UninstallPreview.DisplayRequiredBy(root,
            root.Installations.Single(i => i.Component == InstallComponent.SDK), root.InstallSpecs));

        output.Should().Contain("Component").And.Contain("Installed version").And.Contain("Install spec").And.Contain("Source");
        output.Should().Contain("Command").And.NotContain("Explicit").And.NotContain("9.0.0");
        output.Should().Contain(multiple ? "the following install specs:" : "the following install spec:");
        output.Should().Contain(multiple ? "If you keep the install specs," : "If you keep the install spec,");
        output.Should().NotContain("installations (").And.NotContain("Proceed with uninstall?");
    }

    [TestMethod]
    [ResourceLock(WellKnownResources.Console)]
    [DataRow(false)]
    [DataRow(true)]
    public void RepositoryWarnings_UseFileCountIndependentlyOfSpecCountAndEscapeMarkup(bool multipleRepositories)
    {
        using var fixture = new UninstallTestEnvironment();
        fixture.AddInstallation(InstallComponent.SDK, "8.0.100");
        fixture.AddSpecs(InstallComponent.SDK, "8.0.1xx");
        var root = fixture.ReadRoot();
        var directory = Path.Combine(fixture.Environment.TempRoot, "[repository]");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "global.json");
        File.WriteAllText(path, """{"sdk":{"version":"8.0.100","rollForward":"latestPatch","allowPrerelease":false}}""");
        root.InstallSpecs.Add(new InstallSpec
        {
            Component = InstallComponent.SDK, InstallSource = InstallSource.GlobalJson,
            GlobalJsonPath = path, VersionOrChannel = "8.0.1xx"
        });
        if (multipleRepositories)
        {
            var other = Path.Combine(fixture.Environment.TempRoot, "global.json");
            File.Copy(path, other);
            root.InstallSpecs.Add(new InstallSpec
            {
                Component = InstallComponent.SDK, InstallSource = InstallSource.GlobalJson,
                GlobalJsonPath = other, VersionOrChannel = "8.0.1xx"
            });
        }

        var output = Render(() => UninstallPreview.DisplayRequiredBy(root, root.Installations.Single(), root.InstallSpecs));

        output.Should().Contain(path).And.Contain("rollForward: latestPatch").And.Contain("allowPrerelease: false");
        output.Should().Contain(multipleRepositories ? "listed global.json files" : "listed global.json file,");
        output.Should().Contain(multipleRepositories ? "These repositories" : "This repository");
        output.Should().Contain("dotnetup sdk install");
        output.IndexOf("8.0.100 (rollForward", StringComparison.Ordinal).Should()
            .BeLessThan(output.IndexOf("Command", StringComparison.Ordinal));
    }

    [TestMethod]
    [ResourceLock(WellKnownResources.Console)]
    public void RetainedVersion_UsesFilteredTableAndReportsOutcomeInsteadOfAnotherConfirmation()
    {
        using var fixture = new UninstallTestEnvironment();
        fixture.AddInstallation(InstallComponent.SDK, "8.0.100");
        fixture.AddSpecs(InstallComponent.SDK, "8", "8.0.100");
        var root = fixture.ReadRoot();
        var output = Render(() => UninstallPreview.DisplayRetained(root,
            root.Installations.Single(), [root.InstallSpecs.Single(s => s.VersionOrChannel == "8")]));

        output.Should().Contain("was kept because the following install spec still resolves to it:");
        output.Should().Contain("Command").And.NotContain("Choose an action:").And.NotContain("Proceed with uninstall?");
    }

    private static string Render(Action display)
    {
        using var writer = new StringWriter();
        var original = AnsiConsole.Console;
        try
        {
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors, Out = new AnsiConsoleOutput(writer)
            });
            console.Profile.Width = 300;
            AnsiConsole.Console = console;
            display();
            return Regex.Replace(ConsoleOutputNormalizer.StripAnsi(writer.ToString()), @"\s+", " ");
        }
        finally
        {
            AnsiConsole.Console = original;
        }
    }
}
