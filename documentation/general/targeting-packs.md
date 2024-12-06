This is a proposal to add a new concept called targeting packs to the .NET SDK.

# Wait, aren't targeting packs something that we already have?

Well, yes.  But the current targeting packs (for example `Microsoft.NETCore.App.Ref`) aren't really a user-facing feature.  So we'll refer to them as "Ref Packs" and use the name "Targeting Pack" for a new feature that we'll add.

# OK, so what do these new targeting packs do?

They contain the information the .NET SDK needs before restore is run to target a given version of .NET.  Mostly, users shouldn't need to worry about them.  But you will be able to install them:

```
> dotnet targeting-pack install 9.0.1
Installing targeting pack for .NET 9.0.1... Done
```

You will be able to update them:

```
> dotnet targeting-pack update
Installing targeting pack for .NET 9.0.1... Done
Installing targeting pack for .NET 8.0.12... Done
Installing targeting pack for .NET 7.0.21... Done
```

You'll also be able to list which ones are installed:

```
> dotnet targeting-pack list
9.0.1
9.0.0
8.0.12
8.0.11
7.0.21
7.0.20
```

When building, by default the latest installed targeting pack for the targeted major version of .NET will be used.

# What's the point?  Shouldn't I just update the .NET SDK to target the latest version of .NET?

Yes, you should!  In fact, we plan to work on features to help notify you if there is an update available to the .NET SDK, and to make it easier to update to the new version.  However, there are cases where there isn't an SDK update available:

**Preview SDKs:** Preview SDKs release on a different schedule from regular monthly patches.  So a .NET 10 preview SDK will not target the latest patches of .NET 9 or .NET 8.

**Out of support SDKs:** Because of the LTS / STS cycle, a later version of the SDK may go out of support before an earlier runtime version.  For example, there was a period of time when .NET 6 was still in support, but .NET 7 (and the .NET 7 SDK) were not in support.  So you could target .NET 6 with the .NET 7 SDK, but there weren't updates to the .NET 7 SDK to target the latest patches of .NET 6.  Targeting packs would have allowed the .NET 7 SDK to continue to target the latest releases of .NET 6.  (If possible, however, it would be recommended to use an in-support SDK, such as the .NET 6 or .NET 8 SDK.)

Additionally, targeting packs will mean that we don't need to include the latest version numbers of other major versions in the .NET SDK.  Today the .NET 10 SDK needs to have the latest version numbers for .NET 9 and .NET 8 (any versions that are still in support).  We can't have version flow between major versions, so we have to rely on version math to keep them in sync.  These numbers are hard to keep right when there are exceptions to the monthly release cycle, such as when we release a hotfix SDK, or when we skip a monthly release for one major version but not another.

Targeting packs might also help simplify the way we handle runtime versioning in internal repos.  Currently we need custom MSBuild logic to update items such as `KnownFrameworkReference` to the right versions.  Using a targeting pack could simplify this logic and make it more correct (there may be items that should be updated but aren't today).  However, it's not yet clear how this would work, as we would need to split the targeting pack for each shared framework, when normally a targeting pack should have versions for all the shared frameworks.

# Targeting pack contents

The targeting pack would include the information that the .NET SDK needs to know about before restore in order to target a release of .NET.  This mainly includes various "pack" versions such as the Ref pack, the runtime packs, as well as packs for things like ILLink, Crossgen, etc.  Currently  this information is stored in `Microsoft.NETCoreSdk.BundledVersions.props`.  With targeting packs we would probably put it in a json file in the targeting pack and have an MSBuild target that would read the json data and populate the same items that are currently defined in the BundledVersions file.

The targeting pack would also include the `PrunePackageReference` / "supplied by platform" data for NuGet.

# Targeting pack acquisition

The targeting pack needs to have version information for each of the shared frameworks.  So those versions would flow into the dotnet/sdk repo, and the targeting pack would be produced there.  The standalone .NET SDK installer would only include the targeting pack for the corresponding major version of .NET (ie the .NET 10 SDK would include the targeting pack for .NET 10, but not .NET 9 or earlier).  The targeting pack would also be published as a package to NuGet.

The first run experience for the .NET CLI would download and install targeting packs for previous major versions of .NET.  So even though the .NET SDK wouldn't include those targeting packs, after first run it would still be able to build projects targeting earlier .NET versions.

The targeting packs for all the different major versions of .NET would also be inserted into Visual Studio and installed whenever the .NET SDK was installed.  So Visual Studio and full framework MSBuild could also continue to build projects targeting previous major versions of .NET, without relying on the CLI first run experience.

# Open questions

- Should targeting packs support being split into 3, one per shared framework?
- How can the targeting pack or versions in it be overridden for internal flow
- Targeting pack versioning for previews
- How do we pin?  Do we need another piece of data in global.json?  All the time?
- How much does this actually help if it doesn't address workload cross-version flow?
