# Releases and versioning

This page explains how RimObs versions are assigned, what ships with each release, and how wire-schema versioning relates to the package version.

## Semantic versioning

RimObs follows [Semantic Versioning 2.0](https://semver.org): `MAJOR.MINOR.PATCH`.

Version numbers are derived automatically from commit messages using the [Angular commit convention](https://www.conventionalcommits.org/). The mapping is:

| Commit type | Version bump |
|---|---|
| `fix:`, `perf:` | patch |
| `feat:` | minor |
| `BREAKING CHANGE:` footer or `!` after type/scope | major |
| `refactor:`, `style:`, `ci:` | patch (custom rule) |
| `scope: dashboard` on any type | no release |

The git tag is the single source of truth for the version. There is no separate `VERSION` file or manual version bump step.

## Release channels

Releases are driven by `semantic-release` and triggered on every push to a release branch.

| Branch | Tag pattern | Purpose |
|---|---|---|
| `main` | `v1.2.3` | Stable releases |
| `beta` | `v1.2.3-beta.N` | Pre-release candidates |

Steam Workshop IDs are configured inline in `release.config.mjs`. The `main` branch publishes to the `stable` Steam depot; `beta` does not publish to Steam (no workshop item exists yet).

## What ships per release

Each release produces the following artifacts:

**NuGet packages** (pushed to nuget.org):
- `RimWorks.RimObs.Wire` - shared MessagePack wire types
- `RimWorks.RimObs.Library` - the RimWorld instrumentation library

**Collector binaries** (attached to the GitHub release as `.zip` archives):
- `collector-win-x64-<version>.zip`
- `collector-linux-x64-<version>.zip`
- `collector-osx-arm64-<version>.zip`
- `collector-osx-x64-<version>.zip`

**Steam Workshop**: on the `main` branch, the mod directory is uploaded via SteamCMD to the configured Workshop item ID.

**Build metadata commit**: `semantic-release` writes the resolved version and build timestamp back into `RimObs.Wire/BuildInfo.cs` and commits it with `[skip ci]`.

The `.nupkg` files are also attached to the GitHub release alongside the collector zips. We do not publish GitHub Release UI notes as a separate step; the tag and its generated release notes serve that purpose.

## Wire-schema versioning

The wire schema has its own integer version (`SchemaVersion.Current`, currently `2`) that is independent of the package semver. It lives in `RimObs.Wire/SchemaVersion.cs` and is included in every `TelemetryBatch` so the collector can detect mismatches.

The schema version is bumped only when wire types change in a way that breaks decoding:

- Renaming a field
- Changing a field's type
- Removing a field

Additive changes - adding a new optional field - do not bump the schema version. The collector is expected to tolerate unknown fields.

A semver minor bump does not necessarily bump the schema version. A schema version bump does not necessarily trigger a semver major bump unless the library API surface also breaks. They are tracked independently.

See [Wire protocol](Wire-Protocol) for the full message format and how `schema_version` is validated on arrival.

## Compatibility policy

Within the same major version, RimObs aims for best-effort compatibility across minor releases. The library and collector negotiate via `SchemaVersion`; a mismatch causes the collector to reject the batch and log a warning.

Major version bumps may rename or remove public APIs, change default configuration keys, or bump the schema version in a breaking way. No formal deprecation window is guaranteed before a major bump.

## Reading the changelog

`semantic-release` generates release notes directly from commit messages. Every merged commit that triggers a release contributes to the changelog under its type heading (`Bug Fixes`, `Features`, etc.).

To see what changed in a release, check the annotated tag:

```bash
git show v1.0.0
```

Or browse the GitHub releases page, where the generated notes are published alongside the artifacts.

## Related

- [Wire protocol](Wire-Protocol) - message format and schema-version validation
- [Installation](Installation) - downloading and wiring up the collector binary
