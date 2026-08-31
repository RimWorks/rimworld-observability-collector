# Installation

RimObs is a shared dependency installed by other mods. If you subscribed to a mod that lists it as a requirement, this is what you got.

## Steam Workshop

Subscribe to the RimObs Workshop item. Mods that depend on RimObs declare it in their own `About.xml`, so the Workshop usually pulls it in automatically when you subscribe to a dependent mod. You would only subscribe manually if a setup guide tells you to.

After subscribing, restart RimWorld and enable RimObs in the mod list before any mod that depends on it.

## Manual Install

1. Download the latest release zip from `https://github.com/RimWorks/rimworld-observability-collector/releases`.
2. Extract it into RimWorld's `Mods/` folder so you end up with `Mods/RimObsCollector/` (or whatever folder the zip contains).
3. Launch RimWorld, open the mod list, and enable RimObs before any mod that depends on it.

## What Gets Installed

Two pieces ship together:

- `Assemblies/RimObs.dll` -- the mod-side library. RimWorld's Mono runtime loads this at startup alongside your other mod assemblies.
- `Collector/<rid>/RimObs.Collector` -- a per-platform native binary that runs alongside the game. The four supported runtime IDs are `win-x64`, `linux-x64`, `osx-arm64`, and `osx-x64`.

The collector cannot live under `Assemblies/` because RimWorld's `ModAssemblyHandler` loads every `.dll` in that subtree into Mono, which crashes on the net10 assemblies. See [Architecture](Architecture) for details.

## Updating

Steam Workshop handles updates automatically. For manual installs, download the new release zip, delete the old folder from `Mods/`, and extract the new one in its place.

## Uninstalling

- **Workshop**: unsubscribe from the item and disable it in the mod list.
- **Manual**: delete the mod folder from `Mods/`.

Either way, local session data and diagnostic bundles stored in your platform's appdata directory are not removed. Delete those manually if you no longer want them.
