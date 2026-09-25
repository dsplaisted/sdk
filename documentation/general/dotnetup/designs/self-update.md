# Self Update

`dotnetup self update [--channel <daily|preview|stable>] [--no-progress]` updates the
published NativeAOT `dotnetup` executable that launched the current process. It does
not update managed SDK/runtime installations; `dotnetup update` continues to do that.
Independently installed copies update themselves in place, rather than locating a
separate user-wide or system-wide installation.

**Stage A is implemented:** ordinary commands fail with retry guidance when an update
is in progress or their loaded build is stale. Waiting and transparent forwarding
are [Stage B work](#stage-b-success-criteria), not part of the current algorithm.

The design has three flows:

| Flow | Responsibility |
| --- | --- |
| [Ordinary command](#ordinary-command) | Hold shared activity access, confirm the loaded build is still current, then run. |
| [Self-update](#update-transaction) | Serialize with other updaters, exclude ordinary commands, replace the executable, and verify or restore it. |
| [Backup cleanup](#backup-cleanup) | Opportunistically remove aged backups without interfering with an update. |

Read [the contract](#stage-a-success-criteria) and [current protocol](#current-protocol)
first. [Design decisions and trade-offs](#design-decisions-and-trade-offs) explains
the version rules, platform differences, and limits of recovery.

## Stage A Success Criteria

- Ordinary commands can run concurrently, but their bodies must not execute across
  a self-update boundary. Otherwise old code could encounter installation state
  written in a newer format.
- Competing updates wait with bounded timeouts, then recheck whether an update is
  still needed. They do not fail immediately just because another update is running.
- Only `dotnetup dotnet`, the telemetry drainer, and `self update` are classified as
  safe during replacement. The updater uses its own locking protocol. Parser-only
  actions such as help and version do not execute a command body or enter its gate.
- Ordinary commands, including `dotnetup --info`, may fail during an update. Callers
  are responsible for retrying after it completes.
- Replacement requires neither a reboot nor a separate updater process. Recovery
  uses the transaction's backup, not execution of the rejected candidate.
- Self-update does not migrate SDK/runtime installation state. Its locks are
  handle-based, so coordination can survive an `await`.
- Guarantees require cooperating processes, working runtime file locking, and a
  trusted, stable installation directory. Older tools that bypass the protocol and
  arbitrary external filesystem writers are outside this model.
- This is not a crash-atomic or power-loss-safe transaction. If recovery cannot
  restore a runnable executable, callers must be able to
  [reinstall dotnetup](https://aka.ms/dotnet/dotnetup).

## Current protocol

### Terms and files

An **ordinary command** is one classified as `non-safe` in the code. All commands
default to this classification unless explicitly exempted.

The **loaded version** comes from the running process's assembly informational
version, captured at startup. The **installed version** comes from launching the
executable at its current pathname with `--version`. These can differ: replacement
changes what future launches execute, not the code already loaded in a process.
The **available version** is the release selected from the channel for the target RID.

[SelfUpdatePaths](../../../../src/Installer/dotnetup.Library/SelfUpdate/SelfUpdatePaths.cs)
derives all transaction paths from the captured executable path. For Windows, the
files below are siblings in the installation directory; Unix uses `dotnetup` without
the `.exe` suffix.

| File | Purpose |
| --- | --- |
| `dotnetup.exe` | Installed executable at its canonical name. |
| `dotnetup.exe.new.download` | Download before hash validation. |
| `dotnetup.exe.new` | Hash-validated staged replacement; never executed here. |
| `dotnetup.exe.old.<transaction>` | Backup, with an eight-hexadecimal-character GUID suffix. |
| `dotnetup.exe.old.<transaction>.rejected` | Rejected replacement retained by Windows rollback. |
| `dotnetup.activity.lock` | Coordinates ordinary commands with replacement. |
| `dotnetup.update.lock` | Serializes updates and cleanup. |

Sibling paths keep replacement on the same filesystem. Unique backup names allow a
later update even when an old process still holds an earlier backup open. An occupied
backup or rejected-file destination is never overwritten.

### Lock ownership and lifetime

The lock files are permanent, zero-length files created on first use and never
deleted. Ownership is an open handle, not the presence of the file.

| Participant | Activity lock | Update lock | Lifetime |
| --- | --- | --- | --- |
| Ordinary command | Shared | None for execution | From the command gate through command completion and synchronous telemetry flush. |
| Ordinary command's optional cleanup | Retains shared ownership | Exclusive, one nonblocking attempt | Update lock released when cleanup finishes or is skipped. |
| Updater | Exclusive | Exclusive | After acquiring both, through replacement, verification/recovery, and telemetry flush. |
| `dotnetup dotnet`, telemetry drainer | None | None | No gate or optional cleanup. |
| Help/version parser actions | None | None | No command body, gate, or cleanup. |

The activity lock allows concurrent ordinary commands but excludes an updater.
The update lock protects transaction artifacts, including backups used by cleanup.
Existing manifest-mutating commands still use `ModifyInstallationStates` for their
own critical sections; self-update does not acquire that mutex.

### Ordinary command

[NonSafeCommandGate](../../../../src/Installer/dotnetup.Library/SelfUpdate/NonSafeCommandGate.cs)
runs from `CommandBase.Execute`, after parsing identifies the command and before its
body or option telemetry.

1. Validate the containing directory and try to acquire the activity lock shared.
   If it is busy, fail immediately with guidance to retry after the update.
2. Validate the executable and query its installed version while retaining the lock.
   Require ordinal equality with the loaded full version, including build metadata.
   A failed query stops the command; a different version means the process is stale
   and must be re-run.
3. Attempt optional [backup cleanup](#backup-cleanup), then execute the command body.
   Retain activity access for the lifetime described above.

**Why check the version even when the lock was acquired immediately?** An updater
could have completed replacement after this process loaded but before it reached
the gate. The shared lock prevents another replacement during the check and command;
the version comparison detects that earlier replacement. An unknown version is
never treated as a match.

**Cost:** every ordinary invocation that acquires the activity lock starts an
additional `dotnetup --version` process before doing its work. This adds startup
latency and a failure dependency even when no update is happening. Cleanup may
start a second query if aged backups exist. The [query contract](self-update-verification.md)
describes the subprocess; its timeout is listed below.

### Update transaction

[SelfUpdateWorkflow](../../../../src/Installer/dotnetup.Library/SelfUpdate/SelfUpdateWorkflow.cs)
performs the transaction in the original process. Children only query versions.

1. **Resolve and check without locks.** Validate the location, resolve one release,
   and compare it with the installed version. If no update is eligible, exit
   successfully. Release-resolution failures stop here. An installed-version query
   failure falls through to the locked check rather than authorizing replacement.
2. **Acquire exclusive access.** Acquire the update lock, then try the activity lock.
   If activity is busy, release the update lock before backing off and retrying the
   pair. [SelfUpdateCoordinator](../../../../src/Installer/dotnetup.Library/SelfUpdate/SelfUpdateCoordinator.cs)
   tracks the two contention budgets independently.
3. **Recheck under both locks.** Validate the executable and query its installed
   version again, comparing against the release resolved earlier. Another updater
   may already have installed it. Use the installed version, not the updater's
   loaded version; exit successfully if no update remains.
4. **Stage.** Validate and remove stale staging files, perform eligible backup
   cleanup using the existing locks, and download the pinned release. Validate its
   hash, set permissions, and flush the staged file before replacement.
5. **Replace and verify.** Create a transaction-specific backup and switch the
   installed pathname using the [platform operation](#replacement-and-recovery).
   Launch the installed replacement with `--version` and require a successful,
   matching result.
6. **Finish or recover.** On success, report the installed version. On replacement
   or verification failure, attempt restoration and retain recovery artifacts if
   recovery fails. Acquired locks remain owned by the invocation through telemetry
   flush, on both success and failure.

The initial version query is only a no-op optimization. It avoids excluding ordinary
commands when a poll finds nothing to update; release lookup also stays outside the
locks. The release is then pinned for the transaction. Only the locked recheck can
authorize replacement.

**Cost:** both locks are held during the download, not just during replacement.
This protects the shared staging names without a separate preparation phase, but
ordinary commands fail throughout the download as well as replacement and recovery.
A slow download therefore extends the period when those commands cannot run.

### Backup cleanup

[SelfUpdateCleanup](../../../../src/Installer/dotnetup.Library/SelfUpdate/SelfUpdateCleanup.cs)
is best-effort, not a prerequisite for command success.

1. An ordinary command attempts cleanup only after passing its gate and version
   check. It tries the update lock once without waiting; if unavailable, it skips
   cleanup. An updater instead reuses the locks it already owns.
2. Find eligible aged backups. Only if any exist, query the installed executable
   while holding the update lock and require full ordinal equality with the
   cleanup process's loaded version. If the query fails or differs, leave backups
   untouched: owning the lock alone does not prove a previous update succeeded.
3. Delete eligible backups, tolerating individual deletion failures. Optional
   cleanup releases its update lock before command execution; an updater retains
   both locks for the rest of its transaction.

Cleanup never waits for another lock while holding one. Its version-query child
does not acquire locks or recursively perform cleanup. See
[retention details](#backup-retention) for eligibility.

**Trade-off:** cleanup on ordinary commands provides opportunities to reclaim disk
space even when no further update is installed. The update itself cleans up older
backups before staging, but its new backup is too young to delete, and a no-op update
returns before cleanup. This housekeeping is not needed to make the next update
possible: backup names are unique. It does add filesystem work and, when aged backups
exist, another version query to ordinary commands.

### Why this lock protocol

The acquisition order and cleanup's nonblocking behavior serve different purposes:

- **Update before activity:** an updater waiting behind another updater or cleanup
  does not yet exclude ordinary commands.
- **Release before retry:** an updater does not hold update access while waiting for
  an ordinary command's activity access. Conversely, that command's cleanup never
  waits for update access. Neither participant waits on the other while retaining
  the lock the other needs.
- **Shared activity access:** ordinary commands can overlap. One exclusive mutex
  around every command would serialize them, and a thread-affine mutex could not
  be released on a different thread after an `await`.

### Timeouts and diagnostics

| Operation | Limit | Result on contention or timeout |
| --- | --- | --- |
| Updater acquiring update access | One minute of cumulative contention | `DotnetupBusyWithUpdateOrCleanup`; retry later. |
| Updater acquiring activity access | Two seconds of cumulative contention | `DotnetupBusyWithAnotherCommand`; retry later. |
| Ordinary command acquiring activity access | One attempt | `DotnetupUpdateInProgress`; retry after the update. |
| Optional cleanup acquiring update access | One attempt | Silently skip cleanup. |
| Installed-version query or startup verification | 15 seconds, with separately bounded termination/output-drain waits | Fail the query; the caller stops, retries under locks, skips cleanup, or rolls back as specified above. |

The retry budgets measure contention, not all filesystem I/O latency. Diagnostics
report the category and retry guidance, not lock-holder names or PIDs. A busy update
lock may belong to cleanup, not necessarily another updater.

## Design decisions and trade-offs

### Channel selection and version comparisons

When `--channel` is omitted,
[SelfUpdateDefaultChannel](../../../../src/Installer/dotnetup.Library/SelfUpdate/SelfUpdateDefaultChannel.cs)
uses the loaded build's SemVer prerelease label: none selects `stable`, `preview`
selects `preview`, and any other label (including local development builds) selects
`daily`. Daily and preview builds therefore need distinct labels. See
[command usage](../reference/dotnetup.md#self-update) for explicit channel selection.

The three version checks answer different questions:

| Question | Rule | Reason |
| --- | --- | --- |
| Is an update eligible? | Within the same semantic channel, require higher SemVer precedence, ignoring build metadata. A different channel is eligible even at lower precedence. | Avoid reinstalling an equivalent release, but allow channel transitions. |
| Is this process still current? | The gate and cleanup require ordinal equality of the full loaded and installed strings, including metadata. | Detect a process loaded before a replacement, even when the builds differ only in metadata. |
| Did the replacement start as expected? | Require the reported version to match the selected release, ignoring build metadata (`+...`). If the selected release specifies build metadata, require an exact match including that metadata. | The executable's informational version may include build metadata absent from the feed's release version. |

Here, a semantic channel is the first prerelease identifier, or stable when absent.
For example, `1.0.0-preview.2` can update to `1.0.0-preview.3`, but not back to
`preview.1`. Builds `1.0.0+abc` and `1.0.0+def` are equivalent for update ordering,
but do not match at the ordinary-command gate.

**Limit:** version equality is not content identity. Different binaries reporting
the same full version are indistinguishable. These rules are implemented in the
[workflow](../../../../src/Installer/dotnetup.Library/SelfUpdate/SelfUpdateWorkflow.cs)
and [version-query contract](self-update-verification.md).

### What may run outside the gate?

The boundary is installation-state access, not all process startup.
[Program](../../../../src/Installer/dotnetup.Library/Program.cs) permits startup
telemetry and the first-run notice before the gate. Constructors and parsing must
not inspect SDK/runtime installation state or start command-specific network work.
A rejected ordinary command never runs its body or cleanup.

Exempt commands can outlive a replacement, so they must use the captured loaded
version rather than assume their executable pathname still identifies their code.
**`--version` must also bypass the gate and cleanup:** its parent may hold both
locks, and a gated query child would contend with that parent. Subprocess mechanics
belong to the [verification contract](self-update-verification.md).

This coordination applies to the published native executable.
[Managed development and test hosts](../../../../src/Installer/dotnetup.Library/DotnetupProcessInfo.cs)
do not enter it and cannot self-update. Renamed native executables may run ordinary
commands, but self-update requires the canonical name.

### What must be ready before replacement?

The download must be complete and match the pinned SHA-512 hash before it becomes
the staged replacement. [DotnetDownloader](../../../../src/Installer/Microsoft.Dotnet.Installation/Internal/DotnetDownloader.cs)
applies this check to both network and cached downloads; neither staging name is
executed. Current release metadata is unsigned, and the unsigned-download policy
still applies. Hash validation is not [authenticated freshness](#release-authentication-and-version-selection).

The replacement preserves the installed executable's permissions: its ACL on Windows,
and its Unix mode on Unix. The staged file is flushed to disk before mutation.
Invalid or inaccessible staging paths stop the update rather than being ignored;
only backup cleanup is best-effort.

### Replacement and recovery

**Recovery restores this transaction's backup; it does not need a working candidate.**
[SelfUpdateReplacement](../../../../src/Installer/dotnetup.Library/SelfUpdate/SelfUpdateReplacement.cs)
tracks the attempted filesystem changes and retained paths. The updater continues
executing its old loaded image throughout.

| Platform | Forward replacement | Rollback after switching |
| --- | --- | --- |
| Windows | `File.Replace` combines backup creation and replacement, preserving the installed ACL. | Rename the candidate to `.rejected`, then rename the backup to the canonical name. Neither destination may already exist. |
| Linux | Hard-link the installed executable to the backup name, then move the staged file over the installed path. | Move the backup over the installed path. Running processes retain their old inode. |
| macOS | Uses the same managed hard-link/move path as Linux. | Same implementation; macOS execution, signing, and quarantine behavior remain unverified in this handoff. |

Windows rollback uses two moves because using the still-running original as a
`File.Replace` source imposes stricter
[sharing requirements](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew#parameters).
That choice creates a gap between moving the candidate aside and restoring the backup.

| Failure | Recovery and observable result |
| --- | --- |
| Forward replacement fails, possibly after partial mutation | Inspect this transaction's paths and restore its backup when needed and available. Report failure even if restoration succeeds. |
| Replacement cannot start, times out, exits unsuccessfully, or reports an unexpected version | Attempt rollback. Failure to terminate the query child does not suppress that attempt. |
| First Windows rollback move fails | Candidate and backup remain at their original names. |
| Second Windows rollback move fails | Canonical path is absent; backup and rejected candidate remain. |
| Recovery cannot finish | Retain available artifacts, report recovery failure, and reinstall if the canonical executable is unavailable. |

Restoring the pathname does not prove a lingering query child exited. Nor does a
retained backup guarantee recovery after process termination or power loss; see the
[availability limits](#availability-and-recovery-limits).

### Backup retention

Only transaction-named backups and rejected candidates at least seven days old are
eligible. Links and directories are excluded; enumeration is bounded and failed
deletions are tolerated, since an older process may still have a backup open. See
[SelfUpdateCleanup](../../../../src/Installer/dotnetup.Library/SelfUpdate/SelfUpdateCleanup.cs)
for filename matching and work limits.

**Age starts at the update, not the build.** Before replacement, both installed and
staged files receive the current UTC last-write time, which their backups inherit.
This avoids immediately deleting a backup of an old build. Timestamp failure aborts
before replacement, but timestamps already changed are not restored. Unix hard
links to the same file also observe the change.

Retention is a minimum age, not a deletion deadline. Artifacts may remain indefinitely
if no later invocation cleans them up.

<a id="properties-of-algorithms-1-and-2"></a>

### Availability and recovery limits

Windows measurements observed transient file-not-found errors even during the single
forward `File.Replace` call. Neither those measurements nor the
[ReplaceFileW contract](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew#return-value)
guarantees an uninterrupted canonical pathname. Windows rollback also has a deliberate
gap between renames.

Automation should retry transient launch failures with bounded delay before treating
the installation as missing, and retry commands rejected by the Stage A gate after
the update. After a crash, forced termination, or power loss, use the
[installation scripts](https://aka.ms/dotnet/dotnetup) if the canonical executable
is unavailable. Recovery requires a writable, functioning filesystem; retaining
backups is not a promise of automatic recovery from every interruption.

### Filesystem and trust assumptions

[Path validation](../../../../src/Installer/dotnetup.Library/SelfUpdate/SelfUpdatePaths.cs)
rejects unexpected links, reparse points, and non-files at executable and artifact
paths. These checks are not atomic with later operations: the installation directory
must remain trusted and stable. They are not protection against hostile path
substitution by external writers.

[Locking](../../../../src/Installer/Microsoft.Dotnet.Installation/Internal/ScopedLockFile.cs)
uses runtime file sharing. Windows enforces it at open; Unix uses advisory locking,
which can be disabled or ineffective on some filesystems. Dotnetup does not detect
that condition. Its concurrency guarantees therefore require cooperating processes
and working runtime locks.

**Never delete the lock files.** Deleting and recreating one can let two processes
lock different filesystem objects at the same pathname. Unix permits unlinking an
open lock file; keeping the files permanent is a protocol requirement, not an
OS-enforced guarantee.

## Future work

### Stage B Success Criteria

Stage B preserves the current locking protocol but replaces ordinary commands'
fail-fast behavior with bounded waiting and transparent forwarding by default.
Callers could opt into immediate failure for lower latency. The waiting budget is
intended to cover a typical update; expiry still reports that an update is in progress.
Competing self-updates already wait in Stage A.

After acquiring shared activity access, a stale process must forward rather than
resume old code against potentially newer installation state. The planned forwarding
contract is:

- Launch the updated canonical executable with the original arguments and
  `UseShellExecute = false`; inherit standard streams, working directory, and environment.
- Increment `DOTNETUP_FORWARD_DEPTH`, refusing more than two hops, and reject unexpected
  links/reparse points at the target.
- Retain shared activity access while waiting for the child, then return its exit code.
- Emit forwarding telemetry rather than a duplicate command-completion event.
  The parent never runs its stale body or cleanup.

Forwarding occurs at the command gate, before command-specific work, but startup
telemetry and the first-run notice may already have occurred. Renamed or removed
commands in the replacement can make forwarding fail; compatibility is not guaranteed.

### Release authentication and version selection

The current unsigned channel/checksum metadata, version ordering, and startup probe
check consistency, not authenticated freshness or replay prevention. Future signed
metadata would bind release versions to artifact hashes and require an authenticated,
monotonic update-authorization policy. Signing alone does not establish freshness or
authorize downgrades. The existing signed release-manifest loader is not wired into
self-update.

Explicit version selection, downgrade commands, and `self install` are future
possibilities, not registered CLI surfaces. Use the
[installation guidance](https://aka.ms/dotnet/dotnetup) for older versions.

### Lock-holder diagnostics

Best-effort holder names/PIDs are deferred and must remain diagnostic-only. Missing
or stale details must not affect acquisition, timeout, or recovery decisions. The
current implementation needs no Restart Manager interop, PID registry, or `/proc/locks`
parsing.

## Design alternatives and comparisons

On Windows, `File.Replace` was selected over these alternatives:

| Alternative | Trade-off |
| --- | --- |
| Reboot-delayed replacement | Simpler replacement timing, but an unsuitable interruption for a developer tool. |
| Separate replacer using `MoveFileExW` | Requires copying an updater to a temporary location, handing off ownership, exiting, and draining incompatible executable handles. Backup creation remains separate. |

`MOVEFILE_WRITE_THROUGH` improves completion durability but does not provide an ACID
or power-loss-atomic transaction. `ReplaceFileW`'s write-through flag is unsupported.
The selected approach avoids the separate process and combines Windows backup and
replacement, while still needing flushed staging and partial-failure recovery.
Transactional NTFS is not a suitable dependency because Microsoft recommends against
relying on its continued availability.

The design draws comparisons with:

- [Rustup's separate updater](https://github.com/rust-lang/rustup/blob/main/src/cli/self_update.rs):
  process handoff addresses executable locking, not cross-process update serialization.
- [Aspire's self-update](https://github.com/microsoft/aspire/blob/main/src/Aspire.Cli/Commands/UpdateCommand.cs):
  dotnetup uses a similar verify-and-rollback shape, but stages on the destination
  volume and uses the combined Windows replacement operation rather than rename/copy.
  Dotnetup's locks also coordinate competing updates and ordinary commands.
- [VS Code's update state machine](https://github.com/microsoft/vscode/blob/main/src/vs/platform/update/electron-main/abstractUpdateService.ts)
  and [Windows installer](https://github.com/microsoft/vscode/blob/main/build/win32/code.iss):
  dotnetup adopts the narrower invariant of one update transaction at a time without
  adopting the UI state machine or installer framework. VS Code's macOS and Linux
  distributions use different update mechanisms.
