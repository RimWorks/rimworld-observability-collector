const plugins = [
    [
        '@semantic-release/commit-analyzer',
        {
            releaseRules: [
                { scope: 'dashboard', release: false },
                { type: 'refactor', release: 'patch' },
                { type: 'style', release: 'patch' },
                { type: 'ci', release: 'patch' },
            ],
        },
    ],
    '@semantic-release/release-notes-generator',
    [
        'semantic-release-replace-plugin',
        {
            replacements: [
                {
                    files: ['RimObs.Wire/BuildInfo.cs'],
                    from: 'Revision = ".*"',
                    to: 'Revision = "${nextRelease.version}"',
                    results: [{ file: 'RimObs.Wire/BuildInfo.cs', hasChanged: true, numMatches: 1, numReplacements: 1 }],
                    countMatches: true,
                },
                {
                    files: ['RimObs.Wire/BuildInfo.cs'],
                    from: 'BuildTime = ".*"',
                    to: () => `BuildTime = "${new Date().toISOString()}"`,
                    results: [{ file: 'RimObs.Wire/BuildInfo.cs', hasChanged: true, numMatches: 1, numReplacements: 1 }],
                    countMatches: true,
                },
            ],
        },
    ],
    [
        '@semantic-release/exec',
        {
            prepareCmd: [
                "make publish-collector",
                "dotnet pack RimObs.Wire/RimObs.Wire.csproj -c Release -p:Version=${nextRelease.version} -p:PackageVersion=${nextRelease.version} -p:FileVersion=${nextRelease.version.replace(/-.*/, '')}.0 -p:AssemblyVersion=${nextRelease.version.replace(/-.*/, '')}.0 -p:InformationalVersion=${nextRelease.version} -o ./nupkgs",
                "dotnet pack RimObs.Library/RimObs.Library.csproj -c Release -p:Version=${nextRelease.version} -p:PackageVersion=${nextRelease.version} -p:FileVersion=${nextRelease.version.replace(/-.*/, '')}.0 -p:AssemblyVersion=${nextRelease.version.replace(/-.*/, '')}.0 -p:InformationalVersion=${nextRelease.version} -o ./nupkgs",
                "mkdir -p ./nupkgs && cd Collector && for rid in win-x64 linux-x64 osx-arm64 osx-x64; do if [ -d \"$rid\" ]; then (cd \"$rid\" && zip -qr \"../../nupkgs/collector-$rid-${nextRelease.version}.zip\" .); fi; done",
            ].join(' && '),
            publishCmd:
                "dotnet nuget push './nupkgs/*.nupkg' --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate",
        },
    ],
    [
        '@semantic-release/github',
        {
            assets: [
                { path: './nupkgs/*.nupkg' },
                { path: './nupkgs/collector-*.zip', label: 'Collector binary (per RID)' },
            ],
        },
    ],
    [
        '@semantic-release/git',
        {
            assets: ['RimObs.Wire/BuildInfo.cs'],
            message: 'chore(release): ${nextRelease.version} [skip ci]\n\n${nextRelease.notes}',
        },
    ],
    [
        'semantic-release-steam',
        {
            appId: '294100',
            branchTargets: { main: 'stable' },
            mods: [
                {
                    name: 'RimObs',
                    path: '.',
                    previewfile: new URL('./About/Preview.png', import.meta.url).pathname,
                    workshopIds: { stable: '3733585062' },
                },
            ],
        },
    ],
];

/** @type {import('semantic-release').GlobalConfig} */
export default {
    branches: ['main', { name: 'beta', prerelease: true }],
    plugins,
};
