// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Deployment.DotNet.Releases;
using Microsoft.Dotnet.Installation;
using Microsoft.Dotnet.Installation.Internal;
using Microsoft.DotNet.Tools.Bootstrapper;
using Microsoft.DotNet.Tools.Bootstrapper.Commands.List;
using Microsoft.DotNet.Tools.Bootstrapper.Commands.Shared;
using Microsoft.DotNet.Tools.Dotnetup.Tests.Utilities;

namespace Microsoft.DotNet.Tools.Dotnetup.Tests;

[TestClass]
public sealed class PhysicalUninstallTests : IDisposable
{
    private readonly UninstallTestEnvironment _fixture = new();
    private DotnetupSharedManifest Manifest => new(_fixture.Environment.ManifestPath);
    private DotnetInstallRoot Root => new(_fixture.Environment.InstallPath, InstallArchitecture.x64);

    [TestMethod]
    [DataRow(InstallComponent.SDK, "8.0.100", false)]
    [DataRow(InstallComponent.SDK, "8.0.100", true)]
    [DataRow(InstallComponent.Runtime, "8.0.0", false)]
    [DataRow(InstallComponent.Runtime, "8.0.0", true)]
    [DataRow(InstallComponent.ASPNETCore, "8.0.0", false)]
    [DataRow(InstallComponent.ASPNETCore, "8.0.0", true)]
    [DataRow(InstallComponent.WindowsDesktop, "8.0.0", false)]
    [DataRow(InstallComponent.WindowsDesktop, "8.0.0", true)]
    public void PhysicalVersion_ExplicitActionControlsSpecRemoval(InstallComponent component, string version, bool remove)
    {
        _fixture.AddInstallation(component, version);
        _fixture.AddSpecs(component, "8");

        Execute(version, component, remove ? UninstallAction.RemoveSpecs : UninstallAction.KeepSpecs);

        _fixture.ReadRoot().Installations.Should().BeEmpty();
        _fixture.ReadRoot().InstallSpecs.Should().HaveCount(remove ? 0 : 1);
        Directory.Exists(DotnetupSharedManifest.GetComponentDirectory(Root.Path,
            new Installation { Component = component, Version = version })).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PhysicalVersion_ChoiceUsesCurrentRepositoryRequirementsAndDoesNotRemoveOlderPin(bool remove)
    {
        _fixture.AddInstallation(InstallComponent.SDK, "8.0.100");
        _fixture.AddInstallation(InstallComponent.SDK, "8.0.105");
        _fixture.AddSpecs(InstallComponent.SDK, "8.0.100", "8");
        var path = AddRepository("""{"sdk":{"version":"8.0.100","rollForward":"latestPatch","allowPrerelease":false}}""");
        var before = _fixture.ReadRoot();
        var contents = File.ReadAllText(path);

        Execute("8.0.105", InstallComponent.SDK, remove ? UninstallAction.RemoveSpecs : UninstallAction.KeepSpecs);

        var after = _fixture.ReadRoot();
        after.Installations.Should().ContainSingle().Which.Version.Should().Be("8.0.100");
        after.InstallSpecs.Should().HaveCount(remove ? 1 : 3);
        if (!remove)
        {
            after.InstallSpecs.Should().BeEquivalentTo(before.InstallSpecs);
            foreach (var spec in after.InstallSpecs)
            {
                InstallSpecResolver.Resolve(spec, after.Installations).Installation!.Version.Should().Be("8.0.100");
            }
        }
        File.ReadAllText(path).Should().Be(contents);
    }

    [TestMethod]
    public void KeepSpecs_ProtectsAllOtherRecordsAndSharedComponentsAndCollectsOrphanComponents()
    {
        _fixture.AddInstallation(InstallComponent.SDK, "8.0.105",
            "shared/Microsoft.NETCore.App/8.0.5", "host/fxr/8.0.5", "packs/Microsoft.NETCore.App.Ref/8.0.5");
        _fixture.AddInstallation(InstallComponent.SDK, "8.0.100",
            "host/fxr/8.0.5", "packs/Microsoft.NETCore.App.Ref/8.0.5");
        _fixture.AddInstallation(InstallComponent.Runtime, "8.0.5");
        _fixture.AddSpecs(InstallComponent.SDK, "8");
        Directory.CreateDirectory(Path.Combine(Root.Path, "sdk", "7.0.100"));
        var before = _fixture.ReadRoot();

        Execute("8.0.105", InstallComponent.SDK, UninstallAction.KeepSpecs);

        var after = _fixture.ReadRoot();
        after.InstallSpecs.Should().BeEquivalentTo(before.InstallSpecs);
        after.Installations.Should().BeEquivalentTo(before.Installations.Where(i => i.Version != "8.0.105"));
        foreach (var path in after.Installations.SelectMany(i => i.Subcomponents))
        {
            Directory.Exists(Path.Combine(Root.Path, path)).Should().BeTrue(path);
        }
        Directory.Exists(Path.Combine(Root.Path, "sdk", "7.0.100")).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void UnreferencedPhysicalVersion_CollectsOnlyNamedInstallation(int action)
    {
        _fixture.AddInstallation(InstallComponent.SDK, "8.0.100");
        _fixture.AddInstallation(InstallComponent.SDK, "9.0.100");

        Execute("8.0.100", InstallComponent.SDK, (UninstallAction)action);

        _fixture.ReadRoot().Installations.Should().ContainSingle().Which.Version.Should().Be("9.0.100");
    }

    [TestMethod]
    public void NonInteractiveWithoutAction_ReportsDecisionWithoutPersistingReconciliation()
    {
        _fixture.AddInstallation(InstallComponent.SDK, "8.0.100");
        _fixture.AddSpecs(InstallComponent.SDK, "8");
        AddRepository("""{"sdk":{"version":"8.0.100"}}""");
        var before = File.ReadAllText(_fixture.Environment.ManifestPath);

        Action uninstall = () => Execute("8.0.100", InstallComponent.SDK, UninstallAction.Unspecified);

        uninstall.Should().Throw<DotnetInstallException>().WithMessage("*--remove-install-specs*--keep-install-specs*");
        File.ReadAllText(_fixture.Environment.ManifestPath).Should().Be(before);
        _fixture.ReadRoot().Installations.Should().ContainSingle();
    }

    [TestMethod]
    [DataRow("policy")]
    [DataRow("deleted")]
    [DataRow("versionless")]
    [DataRow("malformed")]
    [DataRow("installation")]
    [DataRow("spec")]
    public void InteractiveDecision_RevalidatesBeforeApplyingPlan(string change)
    {
        _fixture.AddInstallation(InstallComponent.SDK, "8.0.100");
        _fixture.AddSpecs(InstallComponent.SDK, "8");
        var path = AddRepository("""{"sdk":{"version":"8.0.100"}}""");

        Action uninstall = () => UninstallWorkflow.Execute(_fixture.Environment.ManifestPath, Root.Path, "8.0.100",
            InstallComponent.SDK, interactive: true, chooseAction: () =>
            {
                switch (change)
                {
                    case "policy": File.WriteAllText(path, """{"sdk":{"version":"8.0.100","rollForward":"disable"}}"""); break;
                    case "deleted": File.Delete(path); break;
                    case "versionless": File.WriteAllText(path, "{}"); break;
                    case "malformed": File.WriteAllText(path, "{broken"); break;
                    case "installation": Directory.Delete(Path.Combine(Root.Path, "sdk", "8.0.100"), true); break;
                    case "spec": _fixture.AddSpecs(InstallComponent.SDK, "8.0"); break;
                }
                return UninstallAction.RemoveSpecs;
            });

        uninstall.Should().Throw<DotnetInstallException>().WithMessage("*changed*");
        using var mutex = new ScopedMutex(Constants.MutexNames.ModifyInstallationStates);
        var after = Manifest.ReadManifestWithoutPruning().DotnetRoots.Single();
        after.Installations.Should().ContainSingle();
        after.InstallSpecs.Should().HaveCount(change == "spec" ? 3 : 2);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnreadableRepository_KeepUsesSavedStateAndRemoveRequiresCompleteFallback(bool saved)
    {
        _fixture.AddInstallation(InstallComponent.SDK, "8.0.100");
        var path = AddRepository("{broken", saved);
        var before = File.ReadAllText(_fixture.Environment.ManifestPath);
        if (!saved)
        {
            Action remove = () => Execute("8.0.100", InstallComponent.SDK, UninstallAction.RemoveSpecs);
            remove.Should().Throw<DotnetInstallException>().WithMessage("*no complete saved requirement*");
            File.ReadAllText(_fixture.Environment.ManifestPath).Should().Be(before);
        }

        Execute("8.0.100", InstallComponent.SDK, UninstallAction.KeepSpecs);

        _fixture.ReadRoot().InstallSpecs.Should().ContainSingle().Which.GlobalJsonPath.Should().Be(path);
        _fixture.ReadRoot().Installations.Should().BeEmpty();
        File.ReadAllText(path).Should().Be("{broken");
    }

    [TestMethod]
    public void RemoveSpecs_UsesLastKnownCompleteRequirement()
    {
        _fixture.AddInstallation(InstallComponent.SDK, "8.0.100");
        var path = AddRepository("{broken", saved: true);

        Execute("8.0.100", InstallComponent.SDK, UninstallAction.RemoveSpecs);

        _fixture.ReadRoot().InstallSpecs.Should().BeEmpty();
        _fixture.ReadRoot().Installations.Should().BeEmpty();
        File.ReadAllText(path).Should().Be("{broken");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void MissingPhysicalTarget_DoesNotRemoveSpecsOrPruneManifest(int action)
    {
        _fixture.AddInstallation(InstallComponent.SDK, "8.0.100");
        _fixture.AddSpecs(InstallComponent.SDK, "8");
        Directory.Delete(Path.Combine(Root.Path, "sdk", "8.0.100"), true);
        var before = File.ReadAllText(_fixture.Environment.ManifestPath);

        Action uninstall = () => Execute("8.0.100", InstallComponent.SDK, (UninstallAction)action);

        uninstall.Should().Throw<DotnetInstallException>().Which.ErrorCode.Should().Be(DotnetInstallErrorCode.UninstallTargetNotFound);
        File.ReadAllText(_fixture.Environment.ManifestPath).Should().Be(before);
    }

    [TestMethod]
    public void MissingRoot_StillAllowsSpecOnlyRemovalAndKeepsOtherUnsatisfiedSpecs()
    {
        _fixture.AddSpecs(InstallComponent.SDK, "8.0.100", "9.0.100");
        Directory.Delete(Root.Path, true);

        Execute("8.0.100", InstallComponent.SDK, UninstallAction.Unspecified);

        _fixture.ReadRoot().InstallSpecs.Should().ContainSingle().Which.VersionOrChannel.Should().Be("9.0.100");
        InstallationLister.GetListData(manifestPath: _fixture.Environment.ManifestPath).InstallSpecs.Should().ContainSingle();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MatchingSpec_KeepsSpecFirstBehavior(bool keep)
    {
        _fixture.AddInstallation(InstallComponent.SDK, "8.0.100");
        _fixture.AddSpecs(InstallComponent.SDK, "8.0.100", "8");
        var before = File.ReadAllText(_fixture.Environment.ManifestPath);
        Action uninstall = () => Execute("8.0.100", InstallComponent.SDK,
            keep ? UninstallAction.KeepSpecs : UninstallAction.RemoveSpecs);
        if (keep)
        {
            uninstall.Should().Throw<DotnetInstallException>().WithMessage("*matches a standalone install spec*");
            File.ReadAllText(_fixture.Environment.ManifestPath).Should().Be(before);
        }
        else
        {
            uninstall();
            _fixture.ReadRoot().InstallSpecs.Should().ContainSingle().Which.VersionOrChannel.Should().Be("8");
            _fixture.ReadRoot().Installations.Should().ContainSingle();
        }
    }

    [TestMethod]
    public void PhysicalVersion_RemovesAllDuplicateRecordsInOneOperation()
    {
        _fixture.AddInstallation(InstallComponent.SDK, "8.0.100");
        _fixture.AddSpecs(InstallComponent.SDK, "8");
        _fixture.AlterManifest(root =>
        {
            root.Installations.Add(root.Installations[0]);
            root.InstallSpecs.Add(root.InstallSpecs[0]);
        });

        Execute("8.0.100", InstallComponent.SDK, UninstallAction.RemoveSpecs);

        _fixture.ReadRoot().Installations.Should().BeEmpty();
        _fixture.ReadRoot().InstallSpecs.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PartialFailure_PreservesSpecsAndIncompleteRecordsForRetryOrRepair(bool repair)
    {
        _fixture.AddInstallation(InstallComponent.SDK, "8.0.100", "shared/Microsoft.NETCore.App/8.0.0");
        _fixture.AddSpecs(InstallComponent.SDK, "8");
        using var mutex = new ScopedMutex(Constants.MutexNames.ModifyInstallationStates);
        var data = Manifest.ReadManifestWithoutPruning();
        var root = data.DotnetRoots.Single();
        root.InstallSpecs.Clear();
        var plan = GarbageCollector.CreatePlan(Root, data);
        Action apply = () => new GarbageCollector(Manifest, path =>
        {
            if (path.Contains("Microsoft.NETCore.App", StringComparison.Ordinal))
            {
                throw new IOException("Simulated locked component.");
            }
            Directory.Delete(path, true);
        }).Apply(plan);

        apply.Should().Throw<DotnetInstallException>()
            .Where(e => e.ErrorCode == DotnetInstallErrorCode.UninstallFailed)
            .WithMessage("*sdk/8.0.100*");
        var incomplete = Manifest.ReadManifest().DotnetRoots.Single();
        incomplete.InstallSpecs.Should().ContainSingle();
        incomplete.Installations.Should().ContainSingle().Which.RemovalPending.Should().BeTrue();
        Manifest.GetIncompleteSubcomponents(Root).Should().BeEquivalentTo(["sdk/8.0.100", "shared/Microsoft.NETCore.App/8.0.0"]);
        InstallationLister.GetListData(manifestPath: _fixture.Environment.ManifestPath)
            .Installations.Should().ContainSingle().Which.IsValid.Should().BeFalse();
        Manifest.InstallAlreadyExists(new DotnetInstall(Root, new ReleaseVersion("8.0.100"), InstallComponent.SDK))
            .Should().BeFalse();
        if (repair)
        {
            _fixture.AlterManifest(r => r.Installations.Add(r.Installations[0]));
            _fixture.AddInstallation(InstallComponent.SDK, "8.0.100", "shared/Microsoft.NETCore.App/8.0.0");
            Manifest.ReadManifest().DotnetRoots.Single().Installations.Should().ContainSingle()
                .Which.RemovalPending.Should().BeFalse();
        }
        else
        {
            Execute("8", InstallComponent.SDK, UninstallAction.Unspecified);
            _fixture.ReadRoot().InstallSpecs.Should().BeEmpty();
            _fixture.ReadRoot().Installations.Should().BeEmpty();
            Directory.Exists(Path.Combine(Root.Path, "shared", "Microsoft.NETCore.App", "8.0.0")).Should().BeFalse();
        }
    }

    private string AddRepository(string contents, bool saved = false)
    {
        var path = Path.Combine(_fixture.Environment.TempRoot, "global.json");
        File.WriteAllText(path, contents);
        using var mutex = new ScopedMutex(Constants.MutexNames.ModifyInstallationStates);
        Manifest.AddInstallSpec(Root, new InstallSpec
        {
            Component = InstallComponent.SDK, VersionOrChannel = "8.0.1xx",
            InstallSource = InstallSource.GlobalJson, GlobalJsonPath = path,
            MinimumVersion = saved ? "8.0.100" : null, AllowPrerelease = saved ? false : null
        });
        return path;
    }

    private void Execute(string version, InstallComponent component, UninstallAction action) =>
        UninstallWorkflow.Execute(_fixture.Environment.ManifestPath, Root.Path, version, component,
            action: action, chooseAction: () => throw new AssertFailedException("Unexpected prompt."));

    public void Dispose() => _fixture.Dispose();
}
