# Proposal: predictable uninstall and relationship-based list output

The relationship between an install spec and an installed version can be a source of confusion when using dotnetup.  Some of the main places this can happen are:

- You might ask it to uninstall a specific version that is installed, and it gives you an error that there's no install spec matching that channel
- You might ask it to uninstall a version or channel, and it successfully removes the spec but doesn't remove any installation, because it's referenced by other specs
- You can see that an SDK is installed, but can’t tell what is keeping it installed.  Separate lists of specs and installations don’t show which channel or repository references the version you want to remove

This proposal aims to improve this by improving the behavior of the uninstall commands and the output of `dotnetup list`.

## Implementation status

The relationship-based text list layout, full repository-policy resolution,
last-known requirement fallback, and migration provenance are implemented.
Matching-spec uninstall, physical-version uninstall with a cancel-by-default
choice, the two noninteractive action options, and removal of `--source` are
implemented. Physical-only removal collects components while preserving other
installation records. Failed deletions preserve specs and mark affected
installation records incomplete for retry or repair.

## Proposed behavior

An **install spec** describes what dotnetup should keep installed: a component
and a channel, exact version, or requirement from a registered `global.json`.
An **installation** is a concrete installed version. Multiple specs can resolve
to the same installation.

`dotnetup sdk uninstall` behaves as follows:

- If the argument matches a command-line or migrated install spec, remove it
  without prompting and run normal garbage collection. If its resolved SDK
  remains installed, explain why and show the other specs resolving to it.
  This applies to both channels and exact versions.
- Otherwise, if the argument names an installed SDK version and specs currently
  resolve to it, offer three choices: remove only that SDK and keep the specs,
  remove the SDK and those specs, or cancel. Explain that keeping the specs
  allows later install or update operations to install the version again.
  If no specs resolve to the installed version, remove only that SDK without
  an additional spec-removal decision.
- If neither matches, report an error.

The proposed `--remove-install-specs` and `--keep-install-specs` options select the
corresponding action without prompting. They preserve spec-first matching rather
than forcing the argument to name a physical installation; see
[Scripts and cancellation](#scripts-and-cancellation).

Garbage collection has two levels: collecting installation records no remaining
spec selects, then collecting component directories no remaining installation
references. Normal garbage collection runs both levels when removing specs and
may therefore remove other installations no remaining spec requires.

SDK-only removal skips installation collection: remove just the named
installation record and leave all specs and other installation records unchanged.
Then run component collection using the subcomponents recorded for every remaining
installation, including installations no spec currently selects. Delete component
directories no remaining installation references, but retain shared runtimes,
hosts, packs, and other components still in use. This can also clean up previously
orphaned component directories; it must not collect another installation.

Keeping specs does not create an exclusion, pin, or "do not reinstall" record.
They continue to select the best matching installed SDK, or appear as
`Not installed` if none remains. A later install or update may install the
removed version again, or a newer eligible version; see scenario 2.

Repository specs remain independent: matching a channel or version does not
directly match a `global.json` registration.

Remove the existing `--source` option from the SDK and runtime uninstall
commands as part of this proposal.

`dotnetup list` shows specs alongside their resolved installations so users
can see what is installed and why.

The uninstall scenarios below use sdk installs in the examples, but should also apply to runtime installs as appropriate.

## Scenarios

Affected-spec lists use the same columns, labels, and sorting as `dotnetup list`,
filtered to the specs relevant to the uninstall. They omit summary counts and
unrelated installation-only rows.

### 1. I remove a spec that alone keeps an SDK installed

I ran `dotnetup sdk install 10.0.103`. No other spec needs that SDK.

```console
> dotnetup sdk uninstall 10.0.103
Removed install spec: .NET SDK 10.0.103 (Command).
Uninstalled SDK 10.0.103.
```

The argument matches the saved exact-version spec. Removing it allows
garbage collection to remove the SDK. No extra confirmation is needed.
`dotnetup list` no longer shows either entry. `dotnetup update` does not
reinstall it on behalf of the removed spec.

If the SDK came from migration instead, its saved spec is `10.0.1xx`, not
`10.0.103`. With no other spec keeping the SDK installed, remove it by naming
that channel:

```console
> dotnetup sdk uninstall 10.0.1xx
Removed install spec: .NET SDK 10.0.1xx (Migration).
Uninstalled SDK 10.0.103.
```

If updates have replaced `10.0.103` with a newer patch, the same command removes
the channel spec and that newer SDK instead. No extra confirmation is needed.
The user does not need a different uninstall command or source filter for
migrated specs. Naming the installed exact version instead follows scenario 2.

### 2. I name an installed version with no exact-version spec

I ran `dotnetup sdk install 10.0.1xx`, which installed `10.0.103`. There is no
saved spec for the exact version. The channel currently selects `10.0.103`;
this behavior also applies if an older eligible SDK is installed.

The command targets the installed SDK and offers a choice about its install spec:

```console
> dotnetup sdk uninstall 10.0.103
.NET SDK 10.0.103 is required by the following install spec:

Component  Installed version  Install spec  Source
---------  -----------------  ------------  -------
.NET SDK   10.0.103           10.0.1xx      Command

If you keep the install spec, later install or update commands may reinstall this SDK.

Choose an action:
  Uninstall .NET SDK 10.0.103 and remove the listed install spec
  Uninstall .NET SDK 10.0.103 without modifying install specs
> Cancel
```

Use singular wording for one spec and plural wording for multiple specs:
"the following install specs", "If you keep the install specs", and
"remove the listed install specs". The option "without modifying install specs"
is the same in both cases. Cancel is the default selection.

Choosing SDK-only removal removes the `10.0.103` installation record, then collects
unreferenced components, but keeps the `10.0.1xx` spec. No other installation is
removed. If no other SDK satisfies it, `dotnetup list`
shows that spec as `Not installed`. Report:

```text
Uninstalled SDK 10.0.103. Kept its install spec.
A later install or update may install this SDK again.
```

`dotnetup sdk update` processes the retained SDK specs, as does `dotnetup update`.
`dotnetup sdk install` with the same channel, or with no argument in a repository
whose `global.json` selects it, also resolves the requirement again. These
operations install the newest eligible SDK, which may be the removed version
or a newer one. List and garbage collection do not reinstall missing SDKs.
SDK-only removal does not change `global.json` or any saved requirement.

Choosing spec removal removes the SDK and channel spec, then runs normal
garbage collection. Cancel changes nothing. Unlike a channel that is not
registered, an exact installed version is a meaningful target even without
a matching saved spec.

### 3. An SDK is required by a global.json install spec

Use this variation of scenario 2 whenever any spec resolving to the SDK comes
from a registered `global.json`, whether it is the only spec or one of several.
Explain that removing a repository spec stops tracking its file without
modifying the file itself.

In this example, a command-line spec for `10.0.1xx` and two registered
repository files currently select SDK `10.0.103`. There is no standalone spec
for exact version `10.0.103`. The same prompt applies even if another installed
SDK could satisfy some of these specs.

```console
> dotnetup sdk uninstall 10.0.103
.NET SDK 10.0.103 is required by the following install specs:

Component  Installed version  Install spec                                                 Source
---------  -----------------  -----------------------------------------------------------  ------------------------
.NET SDK   10.0.103           10.0.100 (rollForward: latestPatch, allowPrerelease: false)  C:\src\app\global.json
.NET SDK   10.0.103           10.0.103 (rollForward: disable)                              C:\src\tools\global.json
.NET SDK   10.0.103           10.0.1xx                                                     Command

If you keep the install specs, later install or update commands may reinstall this SDK.
Removing the install specs stops tracking the listed global.json files, but does not change them.
These repositories may no longer build with the installed SDKs.
If you remove the install specs, future updates will no longer install SDKs for them.
To register a repository again, run dotnetup sdk install in that repository.

Choose an action:
  Uninstall .NET SDK 10.0.103 and remove the listed install specs
  Uninstall .NET SDK 10.0.103 without modifying install specs
> Cancel
```

Use singular or plural wording for install specs and repository files
independently: several specs may include only one `global.json` file.

Choosing SDK-only removal keeps all listed specs and repository registrations.
The files and saved requirements stay unchanged, even if a repository's SDK
requirement becomes unsatisfied. Later install or update operations can restore
an eligible SDK as in scenario 2. No other installation is removed.

Choosing spec removal removes the SDK and the listed specs, including the
repository registrations, then runs normal garbage collection. Cancel changes
nothing. Apply the chosen plan once; do not remove one record, refresh stale
duplicates, and require the same command again.

After spec removal, `dotnetup update` will not restore the removed registration.
Running `dotnetup sdk install` in that repository registers it again.

### 4. An older SDK is also installed

Start with an exact-version pin and a channel spec:

```console
dotnetup sdk install 10.0.103
dotnetup sdk install 10.0.1xx
```

Assume the second command installs `10.0.105`. The exact-version spec pins
`10.0.103`, so it survives cleanup. The channel selects `10.0.105`, as does a
registered repository requiring `10.0.100` with `latestPatch` and no prereleases.
There is **no exact-version spec for 10.0.105**.

Uninstalling `10.0.105` offers the same choice between keeping and removing the
channel and repository specs currently selecting it. Do not silently choose an
action because an older eligible version is present:

```console
> dotnetup sdk uninstall 10.0.105
.NET SDK 10.0.105 is required by the following install specs:

Component  Installed version  Install spec                                                 Source
---------  -----------------  -----------------------------------------------------------  ---------------------
.NET SDK   10.0.105           10.0.100 (rollForward: latestPatch, allowPrerelease: false)  C:\src\app\global.json
.NET SDK   10.0.105           10.0.1xx                                                     Command

If you keep the install specs, later install or update commands may reinstall this SDK.
Removing the install specs stops tracking the listed global.json file, but does not change it.
This repository may no longer build with the installed SDKs.
If you remove the install specs, future updates will no longer install SDKs for them.
To register the repository again, run dotnetup sdk install in that repository.

Choose an action:
  Uninstall .NET SDK 10.0.105 and remove the listed install specs
  Uninstall .NET SDK 10.0.105 without modifying install specs
> Cancel
```

Choosing SDK-only removal deletes `10.0.105` and leaves all specs and SDK
`10.0.103` unchanged. The retained channel and repository requirements then
resolve to the already-installed `10.0.103`, so `dotnetup list` shows that version
alongside them. A later install or update can install `10.0.105` again, or a
newer eligible SDK.

Choosing spec removal removes both listed specs and SDK `10.0.105`, then runs
normal garbage collection. The independent `10.0.103` pin and its installation
remain unchanged. Only specs currently selecting the targeted SDK are offered
for removal; the `10.0.103` pin is not one of them. Cancel keeps both SDKs and all
specs unchanged.

SDK-only removal can expose an older installed SDK, but is not a persistent
rollback policy. It neither downloads an older replacement nor prevents later
operations from restoring the removed SDK.

If `10.0.105` also had its own saved spec, spec-first matching would apply, as in scenario 5:
remove that spec, then explain that the channel still keeps the newer SDK.

### 5. The argument matches a spec, but other specs keep the SDK

Both a command-line spec for `10.0.1xx` and a repository select `10.0.105`.

`dotnetup sdk uninstall 10.0.1xx` removes the matching channel spec and garbage
collects without prompting. The SDK remains for the repository. Explain the
result and show the remaining specs that currently resolve to that SDK:

```console
> dotnetup sdk uninstall 10.0.1xx
Removed install spec: .NET SDK 10.0.1xx (Command).
.NET SDK 10.0.105 was kept because the following install spec still resolves to it:

Component  Installed version  Install spec                                                 Source
---------  -----------------  -----------------------------------------------------------  ---------------------
.NET SDK   10.0.105           10.0.100 (rollForward: latestPatch, allowPrerelease: false)  C:\src\app\global.json

No SDKs were uninstalled.
```

This is a successful spec removal, not a request for another decision. Leave
the other specs and repository registrations unchanged. List all other specs
currently selecting the retained SDK, using plural wording when needed.

**The same rule applies to exact-version specs.** If `10.0.105` is a saved
command-line spec and a channel also selects that SDK:

```console
> dotnetup sdk uninstall 10.0.105
Removed install spec: .NET SDK 10.0.105 (Command).
.NET SDK 10.0.105 was kept because the following install spec still resolves to it:

Component  Installed version  Install spec  Source
---------  -----------------  ------------  -------
.NET SDK   10.0.105           10.0.1xx      Command

No SDKs were uninstalled.
```

Do not give exact-version specs a special prompt or implicitly remove the
other specs. A later command naming the installed exact version, once there
is no matching standalone spec, follows scenarios 2 or 3.

If removing the matched spec allows garbage collection to uninstall its SDK,
report the removal as in scenario 1. If it has no installed SDK to begin with,
report spec-only removal without targeting unrelated specs.

### 6. I name a channel that is not registered

```console
> dotnetup sdk uninstall 10.0.1xx
No saved SDK install spec matches '10.0.1xx'.
Run dotnetup list to see installed SDKs and their install specs.
To remove an installed SDK, specify its full version.
```

Return a not-found failure without removing anything, even if `10.0.105` is
installed for some other reason.

This also covers `latest`, `lts`, `preview`, and `daily`: if the named spec
exists, remove it and garbage collect as in scenario 5; otherwise error.
There is no separate "resolve a moving channel for uninstall" experience, no
lookup of the newest downloadable SDK, and no network requirement for uninstall.

### 7. I deleted a repository or changed its SDK requirement

Treat the registered `global.json` as the live requirement, not as a spec
whose cached contents need separate approval to change.

| Change | What the user sees |
| --- | --- |
| Repository or `global.json` deleted | Its spec disappears automatically on reconciliation. No prompt to accept the deletion. |
| Valid file no longer specifies an SDK | The old SDK requirement is removed automatically. |
| Version or roll-forward policy changed | The existing repository entry reflects the new requirement, not an extra historical entry. |
| File is malformed or cannot be read, for example because access is denied | Report the error and use the last known complete requirement for update and cleanup. Label it `last known` in list output. Do not treat the error as confirmed deletion. |

For example, a repository previously requested `10.0.103`, with the default
feature-band policy and `allowPrerelease: false`. While its file is locked or
contains malformed JSON, `dotnetup update` warns and can still update it to
`10.0.105`, but not `10.0.202` or a preview. Other install specs are updated
normally. The fallback does not itself make update fail, and
`--update-global-json` does not rewrite the unreadable file.

Garbage collection uses those same saved constraints. Removing a command-line
spec does not remove an SDK still selected by the repository's last known
requirement. A temporary read or parse failure therefore does not release the
repository's SDK for cleanup. Explicit SDK-only removal is different: it can
remove that SDK while keeping the repository's saved requirement and reporting
the read error. If the registration predates saving complete
constraints, report the error and skip cleanup for its installation root rather
than guessing from the recorded channel alone.

If the file becomes readable and valid again, use its current requirement and
refresh the saved constraints. Deleting the file or removing its SDK version
still deactivates the registration; those are deliberate changes rather than
read failures.

`dotnetup list` reflects those changes without deleting SDK files. The next
cleanup-capable operation can collect SDKs no remaining spec keeps.
A changed requirement may appear without a
satisfying installation until `dotnetup sdk install` is run in the repository.

Do not add `dotnetup sdk uninstall --global-json ...` for this initial design.
Deleting the repository naturally stops tracking it. Stopping tracking while
keeping the repository on disk is a possible future niche operation; it should
not drive an overloaded uninstall syntax now. A physical-version uninstall
can still prompt to remove repository registrations as in scenario 3.

### 8. The version's files are missing, or it was never installed

If an exact-version spec exists but its SDK files are already missing,
remove the matching spec without another confirmation. Report:

```text
Removed install spec: .NET SDK 10.0.103 (Command).
SDK 10.0.103 was already missing; no SDK files were removed.
```

If neither a matching spec nor the exact installation exists, return a
not-found failure. Do not uninstall another version.

Pre-existing unsatisfied specs remain visible in `dotnetup list`; they are
not additional removal targets simply because a different SDK is uninstalled.

### 9. SDKs share files, or removal fails

Removing an SDK deletes its private content, but keeps runtime, host, pack, or
other files used by remaining installations. This is normal behavior and does
not need a separate "Shared runtime files retained" message. Removing an SDK
does not necessarily remove every runtime that came in its archive. The overall
uninstall output still needs refinement; focus on what was removed, SDKs kept
because of other specs, and any failures rather than routine shared-file handling.

For SDK-only removal, component collection protects every other installation
record, not just installations selected by current specs. A separately installed
runtime or another SDK therefore keeps its shared components even if it is
currently unreferenced by specs. For spec removal, normal installation collection
runs first, and component collection protects the installations that remain.

If a locked file or another error prevents physical removal, return failure
and distinguish removed files from remaining files. Preserve enough state to
show an incomplete installation and support retry or repair; do not claim
rollback after partially deleting files.

A successful spec-only removal is different from a failed SDK removal.
The former says the SDK was kept for other specs; the latter must not
discard specs on the assumption that deleting the SDK succeeded.

If the last SDK is removed, say that no SDK remains in the managed installation.
Do not silently select a system installation, rewrite environment settings, or
remove dotnetup itself. A separate last-SDK confirmation is a policy question,
not assumed by the examples above.

## Scripts and cancellation

Add two mutually exclusive options to SDK and runtime uninstall:

| Proposed option | When no standalone spec matches and the argument names an installed version |
| --- | --- |
| `--remove-install-specs` | Remove the specs currently resolving to the named installation and uninstall it, then run normal installation and component collection. This is the first action in the interactive prompt. |
| `--keep-install-specs` | Remove only the named installation record, leave all specs and other installation records unchanged, then collect unreferenced components. This is the "without modifying install specs" action. Later install or update commands may reinstall the version. |

These are action choices, not a general `--yes` or `--force`. Either option skips
the action-selection prompt in interactive and noninteractive use. Other errors
and removal failures still return failure.

For scenario 2, scripts can select either action explicitly:

```console
dotnetup sdk uninstall 10.0.103 --remove-install-specs
dotnetup sdk uninstall 10.0.103 --keep-install-specs
```

These are alternatives, not a sequence to run. The same choices apply when the
affected specs include repository registrations, as in scenarios 3 and 4.

Spec-first removal behaves the same with or without an interactive terminal:
remove the matching spec, then report any retained SDK and the specs selecting
it. Do not expand to other specs. The options do not override that behavior:

- `--remove-install-specs` is redundant when a standalone spec matches. Remove
  only the matching standalone specs, run normal garbage collection, and leave
  other specs selecting the same installation unchanged.
- `--keep-install-specs` is incompatible with a matching standalone spec, because
  spec-first removal would delete that spec. Report the conflict and fail without
  mutation. Do not silently ignore the option or switch to physical removal.
- Passing both options is invalid and fails before mutation, regardless of the
  target.

Without either option, an interactive physical-version uninstall with affected
specs shows the action-selection prompt, with Cancel selected by default. Without
an interactive terminal, it instead fails without mutation, reports those specs,
and explains how to select `--remove-install-specs` or `--keep-install-specs`.
Do not silently choose an action. If no specs resolve to the physical version,
removing it requires no such choice.

Neither option turns an unregistered channel into a physical-version target or
makes a missing target succeed. The not-found rules in scenarios 6 and 8 still
apply.

In an interactive plan, Cancel changes nothing. File changes while the prompt
is open require revalidation before applying it. Automatic reconciliation of
an edited/deleted `global.json` needs no approval, but must not cause an already
approved plan to remove a different SDK, switch between keeping and removing
specs, or forget an additional live repository.

## Make the relationships visible in dotnetup list

Use one table for SDKs and separately installed runtimes with columns in this
order: Component, Installed version, Install spec, Source. Do not list runtimes
that are only included with an SDK. Keep one row per install spec, plus
installation-only rows for installations no current spec resolves to. Repeating a
component/version pair shows that multiple independent specs select the same
installation. Column order does not change the sorting rules below.

The component is part of the install spec, but is displayed separately from
its channel/version and selection policy. A `global.json` path identifies the
source of the requirement, not the spec itself.

List and uninstall use the current, complete `global.json` requirements to
determine which installation each spec resolves to.

Use these source labels rather than `Explicit`:

| Source | Meaning |
| --- | --- |
| Command | Added by a command such as `dotnetup sdk install 10.0.1xx`, whether invoked by a user or another tool. |
| Migration | Added when bringing an existing SDK into dotnetup management, as in scenario 1. |
| Repository file path | The registered `global.json` supplying the SDK requirement. |

New migrations record a distinct origin. Existing `Explicit` specs, including
older migrations whose origin cannot be recovered, retain the Command source label.

```text
C:\dotnet (x64)
Component             Installed version  Install spec                                                Source
--------------------  -----------------  ----------------------------------------------------------  -----------------------
.NET SDK              10.0.105           10.0.100 (rollForward: latestPatch, allowPrerelease: false)  C:\src\app\global.json
.NET SDK              10.0.105           10.0.1xx                                                    Migration
.NET SDK              10.0.103           10.0.103                                                    Command
.NET SDK              9.0.304            -                                                           -
.NET SDK              Not installed      10.0.300 (rollForward: disable)                             C:\src\other\global.json
.NET SDK              Unknown            Unavailable                                                 C:\src\tools\global.json
.NET Runtime          10.0.5             10.0                                                        Command
ASP.NET Core Runtime  10.0.5             10.0                                                        Command
.NET Desktop Runtime  10.0.3             10.0                                                        Command
.NET Runtime          9.0.8              9.0                                                         Command
ASP.NET Core Runtime  9.0.8              9.0                                                         Command

10 install specs; 8 installations (3 SDKs, 5 runtimes)

Warning: Cannot read C:\src\tools\global.json: access denied.
```

- **Sorting:** SDKs first, then all runtimes together; within each group, sort
  by resolved version newest first using semantic version ordering. Runtime
  ties use .NET Runtime, ASP.NET Core, Windows Desktop, then spec and source
  alphabetically. SDK ties use spec and source. Put Not installed, then Unknown,
  last within each group.
- **Missing/error states:** Not installed means no matching installation;
  Unknown means the requirement could not be evaluated. Show diagnostics below
  the table, identifying the affected source or component.
- **Installation-only rows:** When no current spec resolves to a managed SDK
  or separately installed runtime, show `-` under both Install spec and Source.
  These installations are eligible for garbage collection.
- **Counts:** Count distinct component/version installations, not repeated rows.
  Installation-only rows count toward installation totals, but not spec totals.
- **Formatting:** Repeat shared version values on each row, avoid group
  dividers, and wrap long cells rather than truncate them.

Preserve the existing JSON format; any additions needed to expose these
relationships will be designed separately.

## Current behavior and supporting issues

The current implementation is context, not evidence that the proposed experience
already works:

- [`UninstallWorkflow.Execute`](../../../../src/Installer/dotnetup.Library/Commands/Shared/UninstallWorkflow.cs)
  defines the implemented uninstall behavior.
- [`GarbageCollector.Collect`](../../../../src/Installer/dotnetup.Library/GarbageCollector.cs)
  currently combines installation collection with component collection.
  Installation collection reconciles repository specs and selects unreferenced
  installations for removal; component collection deletes directories no retained
  installation references. SDK-only removal needs the latter without the former.
  [`Installation.Subcomponents`](../../../../src/Installer/dotnetup.Library/DotnetupManifestData.cs)
  records the component-directory references used for that cleanup.
- [`InstallWorkflow`](../../../../src/Installer/dotnetup.Library/Commands/Shared/InstallWorkflow.cs)
  resolves installation requests, with
  [`UpdateChannel`](../../../../src/Installer/Microsoft.Dotnet.Installation/Internal/UpdateChannel.cs)
  supplying channel matching.
- [`UpdateWorkflow`](../../../../src/Installer/dotnetup.Library/Commands/Shared/UpdateWorkflow.cs)
  processes registered specs and installs their newest eligible versions. Along
  with install, it can restore a version removed while its specs were retained.
- [`InstallationListRenderer`](../../../../src/Installer/dotnetup.Library/Commands/List/InstallationListRenderer.cs)
  implements the relationship-based text table. JSON relationship changes remain separate.
  [`InstallSpec` and `Installation`](../../../../src/Installer/dotnetup.Library/DotnetupManifestData.cs)
  are separate manifest concepts.
- The current [`SDK install`](../reference/dotnetup-sdk-install.md) and
  [`SDK uninstall`](../reference/dotnetup-sdk-uninstall.md) references document
  the SDK-qualified command names used throughout this draft.

| Issue | Relevance |
| --- | --- |
| [dotnet/sdk#56228](https://github.com/dotnet/sdk/issues/56228) | Noah's original proposal separates untracking from physical uninstall and retains unsatisfied specs with an override. This draft removes matching specs without prompting; targeting an installed version with no matching standalone spec offers SDK-only removal with specs retained or removal of the SDK and its affected specs. |
| [dotnet/sdk#56225](https://github.com/dotnet/sdk/issues/56225) | Stable repository identity and consolidation of duplicate records. |
| [dotnet/sdk#56226](https://github.com/dotnet/sdk/issues/56226) | Refresh requirements before matching; avoid repeated identical uninstalls. |
| [dotnet/sdk#56227](https://github.com/dotnet/sdk/issues/56227) | Preserve minimum versions, roll-forward, and prerelease semantics when determining satisfaction. |
| [dotnet/sdk#53396](https://github.com/dotnet/sdk/issues/53396) | Prerelease-policy correctness as well as documentation. |
| [dotnet/sdk#55312](https://github.com/dotnet/sdk/issues/55312) | Example of uninstall triggering collection of another unused SDK. Normal garbage collection is preserved by this proposal. |
