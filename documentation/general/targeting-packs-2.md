# Overview

This is a proposal to add a new concept called targeting packs to the .NET SDK.

# Motivation

- SDK doesn't need to have up-to-date downlevel patch versions
- Easier to target live runtime versions in internal builds
- Better architecture for keeping track of versions needed for targeting a version of the runtime
- Possibly: Allow preview SDKs to target latest downlevel runtime versions (if we want to expose this as a new user-facing concept)

# Targeting pack contents

The targeting pack would include the information that the .NET SDK needs to know about before restore in order to target a release of .NET.  This mainly includes various "pack" versions such as the Ref pack, the runtime packs, as well as packs for things like ILLink, Crossgen, etc.  Currently  this information is stored in `Microsoft.NETCoreSdk.BundledVersions.props`.  With targeting packs we would probably put it in a json file in the targeting pack and have an MSBuild target that would read the json data and populate the same items that are currently defined in the BundledVersions file.

The targeting pack would also include the `PrunePackageReference` / "supplied by platform" data for NuGet.

