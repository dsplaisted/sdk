// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;
using Microsoft.Dotnet.Installation;
using Microsoft.DotNet.Tools.Bootstrapper;
using Microsoft.DotNet.Tools.Dotnetup.Tests.Utilities;
using Microsoft.NET.TestFramework;

namespace Microsoft.DotNet.Tools.Dotnetup.Tests;

/// <summary>Exercises the real CLI against isolated manifests and fake disk installations.</summary>
[TestClass]
public class UninstallEndToEndTests : IDisposable
{
    private readonly UninstallTestEnvironment _fixture = new();

    [TestMethod]
    [DataRow("sdk")]
    [DataRow("runtime")]
    [DataRow("alias")]
    public void Help_ShowsBothActionsAndNonInteractiveOption(string noun)
    {
        var command = noun == "alias" ? new[] { "uninstall", "--help" } : [noun, "uninstall", "--help"];
        var output = Run(command, out var exitCode);

        exitCode.Should().Be(0, output);
        output.Should().Contain("--remove-install-specs").And.Contain("--keep-install-specs").And.Contain("--non-interactive");
    }

    [TestMethod]
    [DataRow("sdk", "11.0", InstallComponent.SDK, "11.0.100")]
    [DataRow("alias", "11.0", InstallComponent.SDK, "11.0.100")]
    [DataRow("runtime", "11.0", InstallComponent.Runtime, "11.0.0")]
    [DataRow("runtime", "runtime@11.0", InstallComponent.Runtime, "11.0.0")]
    [DataRow("runtime", "aspnetcore@11.0", InstallComponent.ASPNETCore, "11.0.0")]
    [DataRow("runtime", "windowsdesktop@11.0", InstallComponent.WindowsDesktop, "11.0.0")]
    public void MatchingSpec_UninstallsWithoutPrompt(string noun, string argument, InstallComponent component, string version)
    {
        _fixture.AddInstallation(component, version);
        _fixture.AddSpecs(component, "11.0");

        var output = Run(Command(noun, argument), out var exitCode, "");

        exitCode.Should().Be(0, output);
        output.Should().NotContain("Choose an action:").And.NotContain("Proceed with uninstall?");
        output.Should().Contain("Removed install spec:").And.Contain("Command");
        _fixture.ReadRoot().Installations.Should().BeEmpty();
        _fixture.ReadRoot().InstallSpecs.Should().BeEmpty();
        AssertFilesRemoved(component, version);
    }

    [TestMethod]
    public void MatchingSpec_CollectsOtherUnreferencedInstallationsWithoutPrompt()
    {
        foreach (var version in new[] { "9.0.100", "11.0.100", "11.0.104" })
        {
            _fixture.AddInstallation(InstallComponent.SDK, version);
        }
        _fixture.AddInstallation(InstallComponent.Runtime, "11.0.0");
        _fixture.AddSpecs(InstallComponent.SDK, "11.0");

        var output = Run(["sdk", "uninstall", "11.0"], out var exitCode, "");

        exitCode.Should().Be(0, output);
        output.Should().NotContain("Choose an action:").And.NotContain("Proceed with uninstall?");
        _fixture.ReadRoot().Installations.Should().BeEmpty();
        output.Should().Contain("No SDKs remain");
    }

    [TestMethod]
    [DataRow("sdk", InstallComponent.SDK, "11.0.100")]
    [DataRow("runtime", InstallComponent.Runtime, "11.0.0")]
    public void MatchingSpec_RemovesOnlyMatchingSpecsAndExplainsRetainedVersion(string noun, InstallComponent component, string version)
    {
        _fixture.AddInstallation(component, version);
        _fixture.AddSpecs(component, "11.0", "11", version);

        var output = Run(Command(noun, "11.0"), out var exitCode, "", "--remove-install-specs");

        exitCode.Should().Be(0, output);
        Compact(output).Should().Contain("was kept because the following install specs still resolve to it:");
        output.Should().Contain("Component").And.Contain("Installed version").And.Contain("Install spec").And.Contain("Source");
        output.Should().Contain("Command").And.NotContain("Explicit").And.NotContain("Choose an action:");
        _fixture.ReadRoot().Installations.Should().ContainSingle();
        _fixture.ReadRoot().InstallSpecs.Select(s => s.VersionOrChannel).Should().BeEquivalentTo(["11", version]);
    }

    [TestMethod]
    [DataRow("1\n", true, false)]
    [DataRow("2\n", true, true)]
    [DataRow("\n", false, false)]
    [DataRow("3\n", false, false)]
    [DataRow("\u001b\n", false, false)]
    [DataRow("", false, false)]
    [DataRow("invalid\n\n", false, false)]
    public void PhysicalVersion_RedirectedChoiceDefaultsToCancel(string input, bool success, bool keep)
    {
        _fixture.AddInstallation(InstallComponent.SDK, "11.0.100");
        _fixture.AddSpecs(InstallComponent.SDK, "11.0");
        var before = File.ReadAllText(_fixture.Environment.ManifestPath);

        var output = Run(["sdk", "uninstall", "11.0.100"], out var exitCode, input);

        exitCode.Should().Be(success ? 0 : 1, output);
        Compact(output).Should().Contain("Choose an action:").And.Contain("> 3. Cancel");
        Compact(output).Should().Contain("Uninstall .NET SDK 11.0.100 and remove the listed install spec");
        Compact(output).Should().Contain("Uninstall .NET SDK 11.0.100 without modifying install specs");
        if (success)
        {
            _fixture.ReadRoot().InstallSpecs.Should().HaveCount(keep ? 1 : 0);
            _fixture.ReadRoot().Installations.Should().BeEmpty();
            AssertFilesRemoved(InstallComponent.SDK, "11.0.100");
        }
        else
        {
            output.Should().Contain("Uninstall cancelled").And.NotContain("Done.");
            File.ReadAllText(_fixture.Environment.ManifestPath).Should().Be(before);
        }
    }

    [TestMethod]
    [DataRow("sdk", "11.0.100", InstallComponent.SDK)]
    [DataRow("alias", "11.0.100", InstallComponent.SDK)]
    [DataRow("runtime", "11.0.0", InstallComponent.Runtime)]
    [DataRow("runtime", "aspnetcore@11.0.0", InstallComponent.ASPNETCore)]
    [DataRow("runtime", "windowsdesktop@11.0.0", InstallComponent.WindowsDesktop)]
    public void PhysicalVersion_FlagsWorkNonInteractivelyForEveryCommand(string noun, string argument, InstallComponent component)
    {
        var version = component == InstallComponent.SDK ? "11.0.100" : "11.0.0";
        _fixture.AddInstallation(component, version);
        _fixture.AddSpecs(component, "11.0");

        var output = Run(Command(noun, argument), out var exitCode, "", "--non-interactive", "--keep-install-specs");

        exitCode.Should().Be(0, output);
        output.Should().NotContain("Choose an action:");
        _fixture.ReadRoot().InstallSpecs.Should().ContainSingle();
        _fixture.ReadRoot().Installations.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("--remove-install-specs")]
    [DataRow("--keep-install-specs")]
    public void PhysicalVersion_WithRepositoryAndOlderPin_UsesChosenAction(string option)
    {
        _fixture.AddInstallation(InstallComponent.SDK, "11.0.100");
        _fixture.AddInstallation(InstallComponent.SDK, "11.0.104");
        _fixture.AddSpecs(InstallComponent.SDK, "11.0", "11.0.100");
        var path = AddRepository();
        var contents = File.ReadAllText(path);

        var output = Run(["sdk", "uninstall", "11.0.104"], out var exitCode, "", "--non-interactive", option);

        exitCode.Should().Be(0, output);
        output.Should().NotContain("Choose an action:");
        Compact(output).Should().Contain("listed global.json file, but does not change it.");
        _fixture.ReadRoot().Installations.Should().ContainSingle().Which.Version.Should().Be("11.0.100");
        _fixture.ReadRoot().InstallSpecs.Should().HaveCount(option == "--keep-install-specs" ? 3 : 1);
        File.ReadAllText(path).Should().Be(contents);
    }

    [TestMethod]
    public void NonInteractiveWithoutAction_FailsWithoutChangingStateAndExplainsFlags()
    {
        _fixture.AddInstallation(InstallComponent.SDK, "11.0.100");
        _fixture.AddSpecs(InstallComponent.SDK, "latest");
        var before = File.ReadAllText(_fixture.Environment.ManifestPath);

        var output = Run(["sdk", "uninstall", "11.0.100"], out var exitCode, "", "--non-interactive");

        exitCode.Should().Be(1, output);
        output.Should().Contain("--remove-install-specs").And.Contain("--keep-install-specs").And.NotContain("Choose an action:");
        File.ReadAllText(_fixture.Environment.ManifestPath).Should().Be(before);
    }

    [TestMethod]
    [DataRow("sdk")]
    [DataRow("runtime")]
    [DataRow("alias")]
    public void ConflictingFlags_AreRejectedBeforeMutation(string noun)
    {
        _fixture.AddInstallation(InstallComponent.SDK, "11.0.100");
        _fixture.AddSpecs(InstallComponent.SDK, "11.0");
        var before = File.ReadAllText(_fixture.Environment.ManifestPath);

        var output = Run(Command(noun, "11.0"), out var exitCode, "", "--remove-install-specs", "--keep-install-specs");

        exitCode.Should().Be(1, output);
        output.Should().Contain("cannot be used together");
        File.ReadAllText(_fixture.Environment.ManifestPath).Should().Be(before);
    }

    [TestMethod]
    public void KeepWithMatchingSpec_IsRejectedBeforeMutation()
    {
        _fixture.AddInstallation(InstallComponent.SDK, "11.0.100");
        _fixture.AddSpecs(InstallComponent.SDK, "11.0.100", "11.0");
        var before = File.ReadAllText(_fixture.Environment.ManifestPath);

        var output = Run(["sdk", "uninstall", "11.0.100"], out var exitCode, "", "--keep-install-specs");

        exitCode.Should().Be(1, output);
        Compact(output).Should().Contain("matches a standalone install spec");
        File.ReadAllText(_fixture.Environment.ManifestPath).Should().Be(before);
    }

    [TestMethod]
    [DataRow("11.0")]
    [DataRow("9.0")]
    [DataRow("preview")]
    [DataRow("daily")]
    public void UnregisteredChannel_DoesNotResolveOrRemoveOverlappingSpec(string argument)
    {
        _fixture.AddInstallation(InstallComponent.SDK, "11.0.100");
        _fixture.AddSpecs(InstallComponent.SDK, "latest");
        var before = File.ReadAllText(_fixture.Environment.ManifestPath);

        var output = Run(["sdk", "uninstall", argument], out var exitCode, "", "--remove-install-specs");

        exitCode.Should().Be(1, output);
        Compact(output).Should().Contain("No saved .NET SDK install spec or installed version matches");
        File.ReadAllText(_fixture.Environment.ManifestPath).Should().Be(before);
    }

    [TestMethod]
    [DataRow("11.0@core")]
    [DataRow("core@11.0")]
    public void UnsupportedRuntimeSyntax_IsRejectedWithoutMutation(string argument)
    {
        _fixture.AddInstallation(InstallComponent.Runtime, "11.0.0");
        _fixture.AddSpecs(InstallComponent.Runtime, "11.0");
        var before = File.ReadAllText(_fixture.Environment.ManifestPath);

        Run(["runtime", "uninstall", argument], out var exitCode, "");

        exitCode.Should().Be(1);
        File.ReadAllText(_fixture.Environment.ManifestPath).Should().Be(before);
    }

    [TestMethod]
    public void MultipleArguments_AreRejectedWithoutMutation()
    {
        _fixture.AddInstallation(InstallComponent.SDK, "11.0.100");
        _fixture.AddSpecs(InstallComponent.SDK, "11.0", "9.0");
        var before = File.ReadAllText(_fixture.Environment.ManifestPath);

        Run(["sdk", "uninstall", "11.0", "9.0"], out var exitCode, "");

        exitCode.Should().Be(1);
        File.ReadAllText(_fixture.Environment.ManifestPath).Should().Be(before);
    }

    [TestMethod]
    public void SharedRuntimeFiles_StayProtectedBySurvivingSdk()
    {
        _fixture.AddInstallation(InstallComponent.SDK, "11.0.100", "shared/Microsoft.NETCore.App/11.0.0");
        _fixture.AddInstallation(InstallComponent.Runtime, "11.0.0");
        _fixture.AddSpecs(InstallComponent.SDK, "11.0");
        _fixture.AddSpecs(InstallComponent.Runtime, "11.0");

        var output = Run(["runtime", "uninstall", "11.0"], out var exitCode, "");

        exitCode.Should().Be(0, output);
        _fixture.ReadRoot().Installations.Should().ContainSingle().Which.Component.Should().Be(InstallComponent.SDK);
        Directory.Exists(Path.Combine(_fixture.Environment.InstallPath, "shared", "Microsoft.NETCore.App", "11.0.0"))
            .Should().BeTrue();
        Compact(output).Should().Contain("Its files are still used by another installation.");
        output.Should().NotContain("Uninstalled .NET Runtime");
    }

    [TestMethod]
    public void DuplicateSpecs_DoNotRepeatRemovalMessages()
    {
        _fixture.AddInstallation(InstallComponent.SDK, "11.0.100");
        _fixture.AddSpecs(InstallComponent.SDK, "11.0");
        _fixture.AlterManifest(root => root.InstallSpecs.Add(root.InstallSpecs[0]));

        var output = Run(["sdk", "uninstall", "11.0"], out var exitCode, "");

        exitCode.Should().Be(0, output);
        Regex.Matches(output, "Removed install spec:").Count.Should().Be(1);
        _fixture.ReadRoot().InstallSpecs.Should().BeEmpty();
    }

    [TestMethod]
    public void ExternallyEditedInvalidVersion_FailsWithoutDeletingFiles()
    {
        _fixture.AddInstallation(InstallComponent.SDK, "11.0.100");
        _fixture.AddSpecs(InstallComponent.SDK, "11.0");
        _fixture.AlterManifest(root => root.Installations[0].Version = "invalid");
        var before = File.ReadAllText(_fixture.Environment.ManifestPath);

        var output = Run(["sdk", "uninstall", "11.0"], out var exitCode);

        exitCode.Should().Be(1, output);
        Compact(output).Should().Contain("modified outside of dotnetup");
        File.ReadAllText(_fixture.Environment.ManifestPath).Should().Be(before);
        Directory.Exists(Path.Combine(_fixture.Environment.InstallPath, "sdk", "11.0.100")).Should().BeTrue();
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DataRow("--remove-install-specs")]
    [DataRow("--keep-install-specs")]
    public void LockedComponent_ReportsPartialFailureAndSupportsRetry(string option)
    {
        _fixture.AddInstallation(InstallComponent.SDK, "11.0.100", "shared/Microsoft.NETCore.App/11.0.0");
        _fixture.AddSpecs(InstallComponent.SDK, "11.0");
        var lockedPath = Path.Combine(_fixture.Environment.InstallPath, "sdk", "11.0.100", "locked.dll");
        using (File.Open(lockedPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var output = Run(["sdk", "uninstall", "11.0.100"], out var exitCode, "", option);

            exitCode.Should().Be(1, output);
            Compact(output).Should().Contain("Removal was incomplete.")
                .And.Contain("shared/Microsoft.NETCore.App/11.0.0").And.Contain("sdk/11.0.100")
                .And.NotContain("Done.").And.NotContain("Removed install spec:");
            var root = _fixture.ReadRoot();
            root.InstallSpecs.Should().ContainSingle();
            root.Installations.Should().ContainSingle().Which.RemovalPending.Should().BeTrue();
        }

        var retry = Run(["sdk", "uninstall", "11.0.100"], out var retryExitCode, "", option);

        retryExitCode.Should().Be(0, retry);
        _fixture.ReadRoot().Installations.Should().BeEmpty();
        _fixture.ReadRoot().InstallSpecs.Should().HaveCount(option == "--keep-install-specs" ? 1 : 0);
    }

    private string AddRepository()
    {
        var directory = Path.Combine(_fixture.Environment.TempRoot, "[repository]");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "global.json");
        File.WriteAllText(path, """{"sdk":{"version":"11.0.100","rollForward":"latestPatch","allowPrerelease":false}}""");
        _fixture.AlterManifest(root => root.InstallSpecs.Add(new InstallSpec
        {
            Component = InstallComponent.SDK, VersionOrChannel = "9.0.1xx",
            InstallSource = InstallSource.GlobalJson, GlobalJsonPath = path
        }));
        return path;
    }

    private static string[] Command(string noun, string argument) =>
        noun == "alias" ? ["uninstall", argument] : [noun, "uninstall", argument];

    private string Run(string[] command, out int exitCode, string input = "", params string[] options)
    {
        var environment = _fixture.Environment;
        var result = DotnetupTestUtilities.RunDotnetupProcess(
            [.. command, "--manifest-path", environment.ManifestPath, "--install-path", environment.InstallPath,
                "--interactive", "true", .. options],
            captureOutput: true, workingDirectory: environment.TempRoot,
            environmentVariables: new Dictionary<string, string>
            {
                ["DOTNET_TESTHOOK_DOTNETUP_DATA_DIR"] = environment.TempRoot,
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
            }, standardInput: input, timeoutMilliseconds: 60_000);
        exitCode = result.exitCode;
        return ConsoleOutputNormalizer.Normalize(result.output);
    }

    private static string Compact(string output) => Regex.Replace(output, @"\s+", " ");

    private void AssertFilesRemoved(InstallComponent component, string version) =>
        Directory.Exists(DotnetupSharedManifest.GetComponentDirectory(_fixture.Environment.InstallPath,
            new Installation { Component = component, Version = version })).Should().BeFalse();

    public void Dispose() => _fixture.Dispose();
}
