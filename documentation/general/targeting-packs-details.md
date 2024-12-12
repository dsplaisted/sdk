

# Terminology

**Ref Pack** - The package that contains reference assemblies and analyzers for targeting a framework, for example Microsoft.NETCore.App.Ref.  Currently we call this package a targeting pack, especially in the .NET SDK code.
**Targeting Pack** - A new type of package that contains version numbers for ref packs, runtime packs, etc. that may be needed to build an app targeting a version of .NET.

Note:  There are targets (`ResolveTargetingPackAssets`) and matedata (`TargetingPackVersion` on `KnownFrameworkReference` items) that use "targeting pack" to mean the "ref pack".  We could update these, or leave them as is due to compatibility / breaking change concerns.

# Delivery

The .NET SDK will only include the targeting pack for its current major version.  The .NET SDK will also not include any patch version information for previous versions of .NET, which will remove the current complicated and error-prone logic that is currently used to calculate these during the SDK build.

During builds outside of Visual Studio, a pre-restore target will download the targeting pack if necessary.  Visual Studio will include the targeting packs for all previous .NET versions, so builds inside of Visual Studio won't need and will skip this pre-restore download.

The pre-restore download will work by generating a project with a PackageDownload for the targeting pack, and running the Restore target on it via an MSBuild task.  For reference, Azure Functions does something similar, though in that case it is part of publish instead of running before Restore.

Since the SDK won't include the patch version of the targeting pack for previous .NET versions, the PackageDownload for the targeting pack will need to use a wildcard version (for example `8.0.*`, or `[8.0.0,*)`).  This is a new feature that will be needed from NuGet.

# Reasons

- Get the latest patches for prior .NET versions when using preview SDKs
- Get the latest patches for prior .NET versions when using out-of-support SDKs
- Get rid of version math to determine previous patch versions when doing SDK build.  These numbers are hard to keep right when there are exceptions to the monthly release cycle, such as when we release a hotfix SDK, or when we skip a monthly release for one major version but not another.
- SDK self-update is a large, complicated feature, and there is some risk we won't be able to deliver it in .NET 10

# Questions

- Should this support being split into 3?
- How can the targeting pack or versions in it be overridden for internal flow
- Versioning for previews
- How do we pin?  Do we need another piece of data in global.json?  All the time?
- How much does this actually help if it doesn't address workload cross-version flow?

# Targeting meta-pack contents

- Version numbers that are currently in KnownFrameworkReference, KnownRuntimePack, etc.
  - What about Windows SDK targeting pack versions?
- List of "supplied by platform" packages

Layout
```
dotnet
  packs
    meta-targeting
      9.0.1
        Microsoft.NETCore.App
          IncludedPackages.json
          FrameworkTargetingInformation.json
        Microsoft.AspNetCore.App
          IncludedPackages.json
          FrameworkTargetingInformation.json
        Microsoft.WindowsDesktop.App
```

FrameworkTargetingInformation.json
```json
{
    "net8.0":
    {
        "Microsoft.NETCore.App":
        {
            "RuntimeFrameworkName": "Microsoft.NETCore.App",
            "DefaultRuntimeFrameworkVersion": "8.0.0",
            "TargetingPackName": "Microsoft.NETCore.App.Ref",
            "TargetingPackVersion": "8.0.10",
            "RuntimePacks": {
                [
                    {
                        "RuntimePackNamePatterns": "Microsoft.NETCore.App.Runtime.**RID**",
                        "RuntimePackRuntimeIdentifiers": "linux-arm;linux-arm64;linux-musl-arm64;linux-musl-x64;linux-x64;osx-x64;rhel.6-x64;tizen.4.0.0-armel;tizen.5.0.0-armel;win-arm64;win-x64;win-x86;linux-musl-arm;osx-arm64;linux-s390x;linux-bionic-arm;linux-bionic-arm64;linux-bionic-x64;linux-bionic-x86;linux-ppc64le",
                        "LatestRuntimeFrameworkVersion": "8.0.10",
                    },
                    {
                        "RuntimePackNamePatterns": "Microsoft.NETCore.App.Runtime.NativeAOT.**RID**",
                        "RuntimePackRuntimeIdentifiers": "ios-arm64;iossimulator-arm64;iossimulator-x64;tvos-arm64;tvossimulator-arm64;tvossimulator-x64;maccatalyst-arm64;maccatalyst-x64;linux-bionic-arm64;linux-bionic-x64;osx-arm64;osx-x64",
                        "LatestRuntimeFrameworkVersion": "8.0.10",
                        "RuntimePackLabels": "NativeAOT"
                    },
                    {
                        "RuntimePackNamePatterns": "Microsoft.NETCore.App.Runtime.Mono.**RID**",
                        "RuntimePackRuntimeIdentifiers": "linux-arm;linux-arm64;linux-musl-arm64;linux-musl-x64;linux-x64;osx-x64;rhel.6-x64;win-arm64;win-x64;win-x86;linux-musl-arm;osx-arm64;linux-s390x;browser-wasm;ios-arm64;ios-arm;iossimulator-arm64;iossimulator-x64;iossimulator-x86;tvos-arm64;tvossimulator-arm64;tvossimulator-x64;maccatalyst-x64;maccatalyst-arm64;android-arm64;android-arm;android-x64;android-x86",
                        "LatestRuntimeFrameworkVersion": "8.0.10",
                        "RuntimePackLabels": "Mono"
                    }
                ]
            },
            "AppHostPackNamePattern": "Microsoft.NETCore.App.Host.**RID**",
            "AppHostPackVersion": "8.0.10",
            "AppHostRuntimeIdentifiers": "linux-arm;linux-arm64;linux-musl-arm64;linux-musl-x64;linux-x64;osx-x64;rhel.6-x64;tizen.4.0.0-armel;tizen.5.0.0-armel;win-arm64;win-x64;win-x86;linux-musl-arm;osx-arm64;linux-s390x;linux-bionic-arm;linux-bionic-arm64;linux-bionic-x64;linux-bionic-x86;linux-ppc64le",
            "AppHostExcludedRuntimeIdentifiers": "android",
            "Crossgen2PackName": "Microsoft.NETCore.App.Crossgen2",
            "Crossgen2PackNamePattern": "Microsoft.NETCore.App.Crossgen2.**RID**",
            "Crossgen2PackVersion": "8.0.10",
            "Crossgen2RuntimeIdentifiers": "linux-musl-x64;linux-x64;win-x64;linux-arm;linux-arm64;linux-musl-arm;linux-musl-arm64;osx-arm64;osx-x64;win-arm64;win-x86",
            "ILCompilerPackName": "Microsoft.DotNet.ILCompiler",
            "ILCompilerPackNamePattern": "runtime.**RID**.Microsoft.DotNet.ILCompiler",
            "ILCompilerPackVersion": "8.0.10",
            "ILCompilerRuntimeIdentifiers": "linux-arm64;linux-musl-arm64;linux-musl-x64;linux-x64;win-arm64;win-x64;osx-x64;osx-arm64",
            "ILLinkPackName": "Microsoft.NET.ILLink.Tasks",
            "ILLinkPackVersion": "8.0.10",
            "WebAssemblyPackName": "Microsoft.NET.Sdk.WebAssembly.Pack",
            "WebAssemblySdkPackVersion": "8.0.10"
        },
        "Microsoft.WindowsDesktop.App":
        {
            "RuntimeFrameworkName": "Microsoft.WindowsDesktop.App",
            "DefaultRuntimeFrameworkVersion": "8.0.0",
            "TargetingPackName": "Microsoft.WindowsDesktop.App.Ref",
            "TargetingPackVersion": "8.0.10",
            "RuntimePacks": {
                [
                    {
                        "RuntimePackNamePatterns": "Microsoft.WindowsDesktop.App.Runtime.**RID**",
                        "RuntimePackRuntimeIdentifiers": "win-x64;win-x86;win-arm64",
                        "LatestRuntimeFrameworkVersion": "8.0.10",
                    }
                ]
            },
            "IsWindowsOnly": "true",
            "Profiles": {
                "WPF": "Microsoft.WindowsDesktop.App.WPF",
                "WindowsForms": "Microsoft.WindowsDesktop.App.WindowsForms"
            }
        }
    }
}
```

Most data should be removed from BundledVersions.props

# Build / version flow

For current version: Runtime, ASP.NET, and WindowsDesktop versions flow into dotnet/sdk, and targeting meta-pack is created there and bundled with SDK.  Targeting meta-pack is also packaged up and shipped to NuGet.

For previous versions:

dotnet/sdk consumes "n-1" (previous month's) meta-targeting pack, adds 1 to patch versions, and and bundles updated version with SDK.  This pack does not get shipped to NuGet, as the original branch for that major version should handle that.

Questions:
- Does this previous version flow work?
- How do we handle months without a release of a previous version?
- Which feature band do we use to produce the meta-targeting pack that ships (ie 8.0.1xx, 8.0.4xx, or what for 8.0)?

# Bundling targeting meta-packs with runtimes

Could the meta-pack information for each framework be installed in a separate subfolder?  That could allow the different runtimes to bundle the corresponding information.