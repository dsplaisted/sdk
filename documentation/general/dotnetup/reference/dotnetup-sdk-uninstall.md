---
title: dotnetup sdk uninstall command
description: Command reference for removing tracked .NET SDK requirements.
ms.topic: reference
ms.date: 08/07/2026
---

# dotnetup sdk uninstall command

## Name

`dotnetup sdk uninstall` - Remove an SDK install spec or an installed SDK version.

## Synopsis

```console
dotnetup sdk uninstall <CHANNEL> [options]
```

## Arguments

`CHANNEL`

The stored SDK channel or exact version to remove. Matching is
case-insensitive. Matching command-line and migration specifications are removed
together. An SDK remains installed if another requirement still needs it.

If no standalone spec matches, an exact installed version targets that installation.
Choose whether to remove the specs currently selecting it or keep them. Removing
repository specs stops tracking their `global.json` files without changing those files.
An unregistered channel is an error: uninstall does not resolve channels over the network.
For example, if only `latest` is tracked, `uninstall 11.0` does not remove that spec,
but `uninstall 11.0.100` can target that installed version.

## Options

| Option | Description |
| --- | --- |
| `--manifest-path <MANIFEST_PATH>` | Use a custom manifest file. |
| `--install-path <INSTALL_PATH>` | Select the installation root. |
| `--remove-install-specs` | For a physical-version target, remove its affected specs and run normal garbage collection. When a standalone spec matches, remove only that matching spec. |
| `--keep-install-specs` | Remove only the named installation record, preserve all specs and other installation records, then collect unused components. Invalid if a standalone spec matches. |
| `--interactive [true\|false]` | Enable the physical-version action prompt. Defaults to enabled outside CI when output is not redirected. |
| `--non-interactive` | Disable prompts, even if `--interactive` is enabled. Physical removal with affected specs requires an action option. |
| `-?`, `-h`, `--help` | Show command help. |

## Behavior

Matching a standalone spec always takes precedence, whether it names a channel
or an exact version. Remove matching command and migration specs without prompting,
then run installation and component garbage collection across the root. Other
unreferenced installations can be removed too. If the selected SDK stays installed,
show the remaining specs in the same four-column table as `dotnetup list`.

Otherwise, an exact installed version with affected specs offers **remove the
listed specs**, **uninstall without modifying install specs**, or **Cancel**.
Cancel is selected by default; Enter or Escape cancels. With redirected input and
explicit interactive mode, enter 1 or 2 for an action, or 3, Enter, Escape, or EOF
to cancel. Without interactive mode, specify one of the mutually exclusive action
options. An installed version with no affected specs is removed directly.

Keeping specs removes only the named installation, then collects component
directories unreferenced by any other installation record. Shared runtimes, hosts,
and packs remain protected, including those belonging to installations not selected
by any spec. List can then show an older eligible SDK or `Not installed`. Later
install or update commands may reinstall the removed version; keeping specs is
not a persistent rollback or exclusion policy.

Cancellation and a changed plan leave the manifest untouched. Repository files
are revalidated after an interactive choice; changed requirements require rerunning
the command. Unreadable files use a complete saved requirement with a warning.
Without complete saved constraints, normal cleanup is skipped; an explicit
`--keep-install-specs` can still remove only the named version.

Physical deletion failures return a nonzero exit code and report removed and
remaining directories. Specs and incomplete installation records are preserved
for retry or repair; `dotnetup list` marks incomplete installations invalid.
Removing the last SDK does not change environment settings or remove dotnetup.

## Examples

```dotnetcli
dotnetup sdk uninstall latest
dotnetup sdk uninstall 10.0.1xx
dotnetup sdk uninstall 10.0.103 --keep-install-specs --non-interactive
dotnetup sdk uninstall 10.0.103 --remove-install-specs --non-interactive
dotnetup sdk uninstall preview --install-path .\.dotnet
```
