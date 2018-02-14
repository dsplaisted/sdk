- Restore args
  - `-r <rid>`
- Build args
  - `--no-restore`
  - `-r <rid>`
- Publish args
  - `--no-restore`
  - `-r <rid>`
- Project
  - `RuntimeIdentifier`
  - `RuntimeIdentifiers`

# Issues for followup

- Roll-forward for ASP.NET as a framework (Steve Harter, Damian)
  - If .NET Core runtime releases a patch, does there need to be a corresponding patch for the ASP.NET Core framework package that updates the dependency on Microsoft.NETCore.App?  If not, how will self-contained ASP.NET Core apps roll forward to patches of the .NET Core runtime?
- Possibility of getting API from .NET Core (Peter Marcu)
- If the version to roll forward to is hard coded, how do we make sure that's up-to-date
- How does publish from VS work (Mike Lorbetske, Vijay Ramakrishnan)
- How bad is it if the assets file changes during some of these operations?  Will VS and the command-line fight over the assets file, leading to flaky behavior?

# Restore results

- No rid(s)
  - Vanilla project
  - Auto-restore
  - Result:
    - No target with RID in assets file
    - No roll-forward to patch
- `RuntimeIdentifiers`
  - In project: `<RuntimeIdentifiers>win7-x86</RuntimeIdentifiers>`
    - Note that you might only have one, even though property is plural
  - `dotnet restore -r win7-x86`
  - Result:
    - Assets file has target with RID (as well as without)
    - No roll-forward to latest patch of runtime
- `RuntimeIdentifier`
  - In project: `<RuntimeIdentifier>win7-x86</RuntimeIdentifier>`
  - `dotnet build -r win7-x86`
    - Note that `-r` has a different meaning for restore than it does for build or publish
  - Result:
    - Assets file has target with RID (as well as without)
    - Rolls forward to latest patch of runtime

# Initial state
- Console app targeting `netcoreapp2.0`, restored
- Project with `RuntimeIdentifiers`, restored
- Project restored with `dotnet restore -r <rid>`
- Project with `RuntimeIdentifier`, restored
- Project auto-restored with `dotnet build -r <rid>`

# Operations

- `dotnet publish`
- `dotnet publish --no-restore`
- `dotnet publish -r win7-x86`
- `dotnet publish -r win7-x86 --no-restore`

# Matrix

- Console app targeting `netcoreapp2.0`, restored
  - `dotnet publish`
    - Current behavior
      - Publish shared framework app.  Will require a minimum runtime version of 2.0.0
  - `dotnet publish --no-restore`
    - Current behavior
      - Publish shared framework app.  Will require a minimum runtime version of 2.0.0
  - `dotnet publish -r win7-x86`
    - Current behavior
      - Auto-restore will modify assets file.  RuntimeFrameworkVersion will roll forward to 2.0.5.  Will publish a self-contained app with the 2.0.5 runtime
  - `dotnet publish -r win7-x86 --no-restore`
    - Current behavior
      - Error: Assets file doesn't have a target for '.NETCoreApp,Version=v2.0/win7-x86'. Ensure that restore has run and that you have included 'netcoreapp2.0' in the TargetFrameworks for your project. You may also need to include 'win7-x86' in your project's RuntimeIdentifiers.
- Project with `RuntimeIdentifiers`, restored
  - `dotnet publish`
    - Current behavior
      - Publish shared framework app.  Will require a minimum runtime version of 2.0.0
  - `dotnet publish --no-restore`
    - Current behavior
      - Publish shared framework app.  Will require a minimum runtime version of 2.0.0
  - `dotnet publish -r win7-x86`
    - Current behavior
      - Auto-restore will modify assets file.  RuntimeFrameworkVersion will roll forward to 2.0.5.  Will publish a self-contained app with the 2.0.5 runtime
  - `dotnet publish -r win7-x86 --no-restore`
    - Current behavior
      - Will successfully publish a self-contained app, but that app will use the 2.0.0 runtime
    - Proposed behavior
      - Error message that the project was restored for .NET Core 2.0.0, but that self-contained apps should use the latest patch version (currently 2.0.5).  Also include more details on how to resolve the issue and/or a link to a page with more info.
- Project restored with `dotnet restore -r <rid>`
- Project with `RuntimeIdentifier`, restored
- Project auto-restored with `dotnet build -r <rid>`