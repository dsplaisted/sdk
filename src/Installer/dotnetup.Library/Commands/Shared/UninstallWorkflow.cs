// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text.Json;
using Microsoft.Deployment.DotNet.Releases;
using Microsoft.Dotnet.Installation;
using Microsoft.Dotnet.Installation.Internal;
using Microsoft.DotNet.Tools.Bootstrapper.Commands.List;
using Spectre.Console;

namespace Microsoft.DotNet.Tools.Bootstrapper.Commands.Shared;

internal enum UninstallAction
{
    Unspecified,
    RemoveSpecs,
    KeepSpecs,
    Cancel
}

/// <summary>Spec-first uninstall, or an explicit physical-version removal.</summary>
internal static class UninstallWorkflow
{
    public static void Execute(
        string? manifestPath, string? installPath, string versionOrChannel,
        InstallComponent componentFilter, bool interactive = false,
        UninstallAction action = UninstallAction.Unspecified,
        Func<UninstallAction>? chooseAction = null)
    {
        using var mutex = new ScopedMutex(Constants.MutexNames.ModifyInstallationStates);
        var manifest = new DotnetupSharedManifest(manifestPath);
        var path = ResolveInstallPath(installPath, new DotnetEnvironmentManager());
        var snapshot = ReadSnapshot(manifest, path);
        var root = snapshot.Root;
        var requirements = snapshot.Requirements.ToDictionary(pair => pair.Spec, pair => pair.Resolution);
        var installRoot = new DotnetInstallRoot(root.Path, root.Architecture);
        var matchingSpecs = root.InstallSpecs.Where(s =>
            s.Component == componentFilter &&
            s.InstallSource is InstallSource.Explicit or InstallSource.Migration &&
            string.Equals(s.VersionOrChannel, versionOrChannel, StringComparison.OrdinalIgnoreCase)).ToList();
        var specsToRemove = matchingSpecs;
        var targets = snapshot.Requirements.Where(pair => matchingSpecs.Contains(pair.Spec))
            .Select(pair => pair.Resolution.Installation).OfType<Installation>().Distinct().ToList();
        var keepSpecs = false;

        if (matchingSpecs.Count > 0)
        {
            if (action == UninstallAction.KeepSpecs)
            {
                throw new DotnetInstallException(DotnetInstallErrorCode.InvalidArguments,
                    Strings.UninstallKeepMatchingSpec);
            }
        }
        else
        {
            var target = ReleaseVersion.TryParse(versionOrChannel, out _)
                ? snapshot.Available.FirstOrDefault(i =>
                    i.Component == componentFilter && i.Version == versionOrChannel) : null;
            if (target is null)
            {
                throw new DotnetInstallException(DotnetInstallErrorCode.UninstallTargetNotFound,
                    string.Format(CultureInfo.InvariantCulture, Strings.UninstallTargetNotFound,
                        InstallationListRenderer.ComponentName(componentFilter), versionOrChannel, root.Path));
            }
            targets = snapshot.Available.Where(i =>
                i.Component == target.Component && i.Version == target.Version).ToList();
            var primaryPath = Path.GetRelativePath(root.Path,
                DotnetupSharedManifest.GetComponentDirectory(root.Path, target)!).Replace('\\', '/');
            specsToRemove = snapshot.Requirements.Where(pair => pair.Resolution.Installation is { } selected &&
                (selected == target || selected.Subcomponents.Contains(primaryPath, StringComparer.OrdinalIgnoreCase)))
                .Select(pair => pair.Spec).ToList();
            var unknownRequirements = snapshot.Requirements.Any(pair =>
                pair.Resolution.IsActive && pair.Resolution.Spec is null);

            ReportRequirementErrors(snapshot);
            if (unknownRequirements && action != UninstallAction.KeepSpecs)
            {
                throw new DotnetInstallException(DotnetInstallErrorCode.ContextResolutionFailed,
                    Strings.UninstallUnreadableRequirements);
            }
            if (specsToRemove.Count > 0)
            {
                UninstallPreview.DisplayRequiredBy(root, target, specsToRemove, requirements);
            }
            if (action == UninstallAction.Unspecified && (specsToRemove.Count > 0 || unknownRequirements))
            {
                if (!interactive)
                {
                    throw new DotnetInstallException(DotnetInstallErrorCode.ContextResolutionFailed,
                        Strings.UninstallActionRequired);
                }
                action = chooseAction?.Invoke() ?? UninstallPreview.ChooseAction(target,
                    specsToRemove.DistinctBy(s => (s.Component, s.VersionOrChannel, s.InstallSource, s.GlobalJsonPath)).Count());
                if (action is UninstallAction.Cancel or UninstallAction.Unspecified)
                {
                    throw new DotnetInstallException(DotnetInstallErrorCode.OperationCancelled, Strings.UninstallCancelled);
                }
            }
            keepSpecs = action != UninstallAction.RemoveSpecs || specsToRemove.Count == 0;
            var current = ReadSnapshot(manifest, path);
            if (snapshot.ManifestState != current.ManifestState ||
                !snapshot.RequirementState.SequenceEqual(current.RequirementState))
            {
                throw new DotnetInstallException(DotnetInstallErrorCode.OperationCancelled, Strings.UninstallPlanChanged);
            }
        }

        GarbageCollectionPlan plan;
        if (keepSpecs)
        {
            plan = GarbageCollector.CreateComponentPlan(installRoot, snapshot.Manifest, targets);
            specsToRemove = [];
        }
        else
        {
            foreach (var spec in specsToRemove)
            {
                root.InstallSpecs.Remove(spec);
            }
            // Missing installations must not satisfy a live requirement during planning.
            root.Installations.RemoveAll(i => !snapshot.Available.Contains(i));
            plan = GarbageCollector.CreatePlan(installRoot, snapshot.Manifest, requirements);
        }

        GarbageCollectionRunner.ApplyAndDisplay(new GarbageCollector(manifest), plan);
        foreach (var spec in specsToRemove.DistinctBy(s =>
            (s.Component, s.VersionOrChannel, s.InstallSource, s.GlobalJsonPath)))
        {
            AnsiConsole.WriteLine(string.Format(CultureInfo.InvariantCulture, Strings.UninstallRemovedSpec,
                InstallationListRenderer.ComponentName(spec.Component), requirements[spec].Spec?.Name ?? spec.VersionOrChannel,
                spec.InstallSource switch
                {
                    InstallSource.Explicit => Strings.ListCommandLineSource,
                    InstallSource.Migration => Strings.ListMigrationSource,
                    _ => spec.GlobalJsonPath
                }));
        }
        foreach (var removed in plan.InstallationsToRemove.DistinctBy(i => (i.Component, i.Version)))
        {
            var primary = DotnetupSharedManifest.GetComponentDirectory(root.Path, removed);
            AnsiConsole.WriteLine(string.Format(CultureInfo.InvariantCulture,
                primary is not null && Directory.Exists(primary) ? Strings.UninstallRemovedRecord : Strings.UninstallRemovedVersion,
                InstallationListRenderer.ComponentName(removed.Component), removed.Version));
        }
        foreach (var retained in targets.Where(root.Installations.Contains).DistinctBy(i => (i.Component, i.Version)))
        {
            var remaining = plan.GetReferencingSpecs(retained);
            if (remaining.Count > 0)
            {
                UninstallPreview.DisplayRetained(root, retained, remaining, requirements);
            }
        }
        if (keepSpecs && snapshot.Requirements.Any(pair => pair.Resolution.Installation is { } selected && targets.Contains(selected)))
        {
            AnsiConsole.WriteLine(Strings.UninstallKeptSpecs);
        }
        if (plan.InstallationsToRemove.All(i => i.Component != componentFilter))
        {
            AnsiConsole.WriteLine(string.Format(CultureInfo.InvariantCulture,
                targets.Count == 0 && ReleaseVersion.TryParse(versionOrChannel, out _)
                    ? Strings.UninstallAlreadyMissing : Strings.UninstallNoVersionsRemoved,
                InstallationListRenderer.ComponentName(componentFilter), versionOrChannel));
        }
        if (componentFilter == InstallComponent.SDK && targets.Count > 0 &&
            root.Installations.All(i => i.Component != InstallComponent.SDK))
        {
            AnsiConsole.WriteLine(Strings.UninstallNoSdkRemains);
        }
        AnsiConsole.MarkupLine(DotnetupTheme.Brand(Strings.CommandDone.EscapeMarkup()));
    }

    private static Snapshot ReadSnapshot(DotnetupSharedManifest manifest, string path)
    {
        var data = manifest.ReadManifestWithoutPruning();
        var root = data.DotnetRoots.FirstOrDefault(r =>
            DotnetupUtilities.PathsEqual(Path.GetFullPath(r.Path), Path.GetFullPath(path)))
            ?? throw new DotnetInstallException(DotnetInstallErrorCode.UninstallTargetNotFound,
                string.Format(CultureInfo.InvariantCulture, Strings.UninstallNoTrackedInstallations, path));
        var available = root.Installations.Where(i => i.RemovalPending ||
            Directory.Exists(DotnetupSharedManifest.GetComponentDirectory(root.Path, i))).ToList();
        var requirements = root.InstallSpecs.Select(s =>
            (Spec: s, Resolution: InstallSpecResolver.Resolve(s, available))).ToList();
        var state = requirements.SelectMany(pair => new string?[]
        {
            pair.Resolution.IsActive.ToString(),
            pair.Resolution.Spec?.Name,
            pair.Resolution.Spec?.MinimumVersion?.ToString(),
            pair.Resolution.Spec?.AllowPrerelease?.ToString(),
            pair.Resolution.GlobalJsonSdk?.Version,
            pair.Resolution.GlobalJsonSdk?.RollForward,
            pair.Resolution.GlobalJsonSdk?.AllowPrerelease?.ToString(),
            pair.Resolution.Installation?.Component.ToString(),
            pair.Resolution.Installation?.Version,
            pair.Resolution.Error
        }).Concat(available.SelectMany(i => new[] { i.Component.ToString(), i.Version })).ToList();
        return new Snapshot(data, root, available, requirements,
            JsonSerializer.Serialize(data, DotnetupManifestJsonContext.Default.DotnetupManifestData), state);
    }

    private static void ReportRequirementErrors(Snapshot snapshot)
    {
        foreach (var pair in snapshot.Requirements.Where(pair => pair.Resolution.Error is not null))
        {
            Console.Error.WriteLine(pair.Resolution.Error);
            if (pair.Resolution.UsesFallbackRequirement)
            {
                Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    Strings.RepositoryUsingLastKnownRequirement, pair.Spec.GlobalJsonPath));
            }
        }
    }

    private sealed record Snapshot(
        DotnetupManifestData Manifest, DotnetRootEntry Root, List<Installation> Available,
        List<(InstallSpec Spec, InstallSpecResolution Resolution)> Requirements,
        string ManifestState, List<string?> RequirementState);

    internal static string ResolveInstallPath(string? explicitInstallPath, IDotnetEnvironmentManager dotnetEnvironment)
    {
        if (explicitInstallPath is not null)
        {
            return explicitInstallPath;
        }
        var configuredInstall = dotnetEnvironment.GetCurrentPathConfiguration();
        return configuredInstall is { IsDotnetupHive: true }
            ? configuredInstall.Path : dotnetEnvironment.GetDefaultDotnetInstallPath();
    }
}
