---
title: dotnetup uninstall command
description: Command reference for removing a tracked .NET SDK requirement.
ms.topic: reference
ms.date: 08/07/2026
---

# dotnetup uninstall command

## Name

`dotnetup uninstall` - Remove an SDK install spec or an installed SDK version.

This command is an alias for
[`dotnetup sdk uninstall`](dotnetup-sdk-uninstall.md).

## Synopsis

```console
dotnetup uninstall <CHANNEL> [options]
```

## Arguments

`CHANNEL`

The stored SDK channel or exact version to remove. Use `dotnetup list` to
find the stored value. Matching command-line and migration specifications are
removed together. If no standalone spec matches, a full installed version targets
that installation and offers a choice about its specs. Repository registrations
can be removed through that choice without modifying their `global.json` files.

## Options

| Option | Description |
| --- | --- |
| `--manifest-path <MANIFEST_PATH>` | Use a custom manifest file. |
| `--install-path <INSTALL_PATH>` | Select the installation root. Without this option, use the current managed root or the default installation root. |
| `--remove-install-specs` | Remove affected specs for a physical-version target. A matching standalone spec is removed first, without expanding to other specs. |
| `--keep-install-specs` | Remove only the named installation record, then collect unused components. Invalid when a standalone spec matches. |
| `--interactive [true\|false]` | Enable the physical-version action prompt, with Cancel as the default. Defaults to enabled outside CI when output is not redirected. |
| `--non-interactive` | Disable prompts. Physical removal with affected specs requires an action option. |
| `-?`, `-h`, `--help` | Show command help. |

## Examples

Remove a command-line or migration feature-band requirement:

```dotnetcli
dotnetup uninstall 10.0.1xx
dotnetup uninstall 10.0.103 --keep-install-specs --non-interactive
```

An installation remains when another specification still refers to it. Garbage
collection can also remove other unreferenced installations in the root.
Matching-spec removal does not prompt. A physical-version target with affected
specs requires a choice between removing those specs, keeping them, or cancelling.
The action options are mutually exclusive. Keeping specs can allow later install
or update commands to reinstall the version. See [SDK uninstall](dotnetup-sdk-uninstall.md)
for collection, cancellation, and recovery details.
