// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using Microsoft.Dotnet.Installation;
using Microsoft.Dotnet.Installation.Internal;
using Microsoft.DotNet.Tools.Bootstrapper.Telemetry;

namespace Microsoft.DotNet.Tools.Bootstrapper;

/// <summary>
/// Performs garbage collection on a dotnet install root by removing installations
/// and subcomponent folders that are no longer referenced by any install spec.
/// </summary>
internal class GarbageCollector
{
    private readonly DotnetupSharedManifest _manifest;
    private readonly Action<string> _deleteDirectory;

    public GarbageCollector(DotnetupSharedManifest manifest, Action<string>? deleteDirectory = null)
    {
        _manifest = manifest;
        _deleteDirectory = deleteDirectory ?? (path => Directory.Delete(path, recursive: true));
    }

    /// <summary>
    /// Runs garbage collection for a specific dotnet root.
    /// Returns the list of subcomponent paths that were deleted from disk.
    /// </summary>
    public List<string> Collect(DotnetInstallRoot installRoot)
    {
        return Apply(CreatePlan(installRoot, _manifest.ReadManifest()));
    }

    /// <summary>
    /// Plans installation-record and directory removals, updating only the in-memory manifest.
    /// </summary>
    internal static GarbageCollectionPlan CreatePlan(DotnetInstallRoot installRoot, DotnetupManifestData manifest,
        IReadOnlyDictionary<InstallSpec, InstallSpecResolution>? requirements = null)
    {
        var root = manifest.DotnetRoots.FirstOrDefault(r =>
            DotnetupUtilities.PathsEqual(Path.GetFullPath(r.Path), Path.GetFullPath(installRoot.Path)) &&
            r.Architecture == installRoot.Architecture);

        if (root is null)
        {
            return new GarbageCollectionPlan(manifest, null, [], [], []);
        }

        var installSpecsByInstallation = ResolveInstallSpecsByInstallation(root, out bool skipCollection, requirements);
        if (skipCollection)
        {
            Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                Strings.GarbageCollectionSkippedUnreadableRequirement, root.Path));
            return new GarbageCollectionPlan(manifest, root, installSpecsByInstallation, [], []);
        }

        var installationsToKeep = installSpecsByInstallation.Keys.Select(i => (i.Component, i.Version)).ToHashSet();
        var installationsToRemove = root.Installations
            .Where(i => !installationsToKeep.Contains((i.Component, i.Version)))
            .ToList();

        return CreateComponentPlan(installRoot, manifest, installationsToRemove, installSpecsByInstallation);
    }

    internal static GarbageCollectionPlan CreateComponentPlan(
        DotnetInstallRoot installRoot, DotnetupManifestData manifest,
        IReadOnlyCollection<Installation> installationsToRemove,
        Dictionary<Installation, List<InstallSpec>>? installSpecsByInstallation = null)
    {
        var root = manifest.DotnetRoots.First(r =>
            DotnetupUtilities.PathsEqual(r.Path, installRoot.Path) && r.Architecture == installRoot.Architecture);
        var referencedSubcomponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var installation in root.Installations.Except(installationsToRemove))
        {
            foreach (var sub in installation.Subcomponents)
            {
                referencedSubcomponents.Add(sub);
            }
        }

        return new GarbageCollectionPlan(manifest, root, installSpecsByInstallation ?? [], [.. installationsToRemove],
            FindOrphanedSubcomponents(installRoot.Path, referencedSubcomponents));
    }

    /// <summary>
    /// Preserves a recovery checkpoint before deleting directories, then commits the planned
    /// spec and installation removals only after deletion succeeds.
    /// </summary>
    internal List<string> Apply(GarbageCollectionPlan plan)
    {
        if (plan.Root is null)
        {
            return [];
        }

        if (plan.PathsToDelete.Count > 0)
        {
            var recovery = _manifest.ReadManifestWithoutPruning();
            var root = recovery.DotnetRoots.FirstOrDefault(r =>
                DotnetupUtilities.PathsEqual(r.Path, plan.Root.Path) && r.Architecture == plan.Root.Architecture);
            if (root is not null)
            {
                var paths = plan.PathsToDelete.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var installation in root.Installations.Where(i =>
                    i.Subcomponents.Any(paths.Contains) || plan.InstallationsToRemove.Any(removed =>
                        removed.Component == i.Component && removed.Version == i.Version)))
                {
                    installation.RemovalPending = true;
                }
            }
            _manifest.WriteManifest(recovery);
        }

        var deleted = DeleteOrphanedSubcomponents(plan.Root.Path, plan.PathsToDelete);
        foreach (var installation in plan.InstallationsToRemove)
        {
            plan.Root.Installations.Remove(installation);
        }

        _manifest.WriteManifest(plan.Manifest);
        return deleted;
    }

    private static Dictionary<Installation, List<InstallSpec>> ResolveInstallSpecsByInstallation(
        DotnetRootEntry root, out bool skipCollection,
        IReadOnlyDictionary<InstallSpec, InstallSpecResolution>? requirements)
    {
        skipCollection = false;
        var installSpecsByInstallation = new Dictionary<Installation, List<InstallSpec>>();
        foreach (var spec in root.InstallSpecs.ToList())
        {
            var resolution = requirements is not null ? requirements[spec] : InstallSpecResolver.Resolve(spec, root.Installations);
            if (!resolution.IsActive)
            {
                root.InstallSpecs.Remove(spec);
                continue;
            }
            if (resolution.Error is not null)
            {
                Console.Error.WriteLine(resolution.Error);
                skipCollection |= resolution.Spec is null;
                if (resolution.UsesFallbackRequirement)
                {
                    Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        Strings.RepositoryUsingLastKnownRequirement, spec.GlobalJsonPath));
                }
            }
            if (spec.InstallSource == InstallSource.GlobalJson && resolution.Spec is { } requirement)
            {
                spec.VersionOrChannel = requirement.Name;
                if (!resolution.UsesFallbackRequirement)
                {
                    // Refresh saved constraints only after successfully reading global.json.
                    spec.MinimumVersion = requirement.MinimumVersion?.ToString();
                    spec.AllowPrerelease = requirement.AllowPrerelease;
                }
            }
            if (resolution.Installation is { } matchingInstallation)
            {
                if (!installSpecsByInstallation.TryGetValue(matchingInstallation, out var specs))
                {
                    specs = [];
                    installSpecsByInstallation.Add(matchingInstallation, specs);
                }
                specs.Add(spec);
            }
        }
        return installSpecsByInstallation;
    }

    /// <summary>
    /// Walks the dotnet root and lists subcomponent folders not in the referenced set, without deleting them.
    /// </summary>
    private static List<string> FindOrphanedSubcomponents(string dotnetRootPath, HashSet<string> referencedSubcomponents)
    {
        var paths = new List<string>();

        if (!Directory.Exists(dotnetRootPath))
        {
            return paths;
        }

        foreach (var topLevelDir in Directory.GetDirectories(dotnetRootPath))
        {
            var topLevelName = Path.GetFileName(topLevelDir);

            // Get the subcomponent depth for this folder
            if (!SubcomponentResolver.TryGetDepth(topLevelName, out int depth))
            {
                if (!SubcomponentResolver.IsIgnoredFolder(topLevelName))
                {
                    Console.Error.WriteLine($"Note: Unknown folder '{topLevelName}' found in dotnet root, skipping.");
                }
                continue;
            }

            // Enumerate subcomponent-level directories
            var subcomponentDirs = GetDirectoriesAtDepth(topLevelDir, depth - 1); // depth-1 because we're already inside the top-level
            foreach (var subDir in subcomponentDirs)
            {
                var relativePath = Path.GetRelativePath(dotnetRootPath, subDir).Replace('\\', '/');
                if (!referencedSubcomponents.Contains(relativePath))
                {
                    paths.Add(relativePath);
                }
            }
        }

        return paths;
    }

    private List<string> DeleteOrphanedSubcomponents(string dotnetRootPath, IReadOnlyList<string> paths)
    {
        using var op = Metrics.Track("gc/delete-orphaned-subcomponents", activityName: "gc.delete-orphaned-subcomponents");
        var deleted = new List<string>();
        var failedCount = 0;
        string? lastFailedPath = null;

        foreach (var relativePath in paths)
        {
            try
            {
                _deleteDirectory(Path.Combine(dotnetRootPath, relativePath));
                deleted.Add(relativePath);
            }
            catch (DirectoryNotFoundException)
            {
                // A previously interrupted removal may already have deleted this directory.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Warning: Could not delete '{relativePath}': {ex.Message}");
                ++failedCount;
                lastFailedPath = relativePath;
                DotnetupTelemetry.Instance.RecordException(op, ex, errorCode: "gc.delete_failed");
            }
        }

        op.Tag("gc.deleted_count", deleted.Count);
        op.Tag("gc.failed_count", failedCount);
        op.Tag("gc.failed_path", lastFailedPath);

        if (failedCount > 0)
        {
            throw new DotnetInstallException(DotnetInstallErrorCode.UninstallFailed,
                string.Format(CultureInfo.InvariantCulture, Strings.UninstallPartialFailure,
                    string.Join(", ", deleted), failedCount, lastFailedPath));
        }

        return deleted;
    }

    /// <summary>
    /// Recursively enumerates directories at a specific depth below a starting directory.
    /// A remainingDepth of 1 returns direct subdirectories of startDir.
    /// </summary>
    private static IEnumerable<string> GetDirectoriesAtDepth(string startDir, int remainingDepth)
    {
        if (!Directory.Exists(startDir))
        {
            return [];
        }

        if (remainingDepth <= 1)
        {
            return Directory.GetDirectories(startDir);
        }

        return Directory.GetDirectories(startDir)
            .SelectMany(d => GetDirectoriesAtDepth(d, remainingDepth - 1));
    }
}
