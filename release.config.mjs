const plugins = [
    [
        '@semantic-release/commit-analyzer',
        {
            releaseRules: [
                { scope: 'dashboard', release: false },
                { type: 'refactor', release: 'patch' },
                { type: 'style', release: 'patch' },
                { type: 'ci', release: 'patch' },
                // README.template.md is the workshop description, so docs are shipped content.
                { type: 'docs', release: 'patch' },
            ],
        },
    ],
    '@semantic-release/release-notes-generator',
    [
        '@semantic-release/exec',
        {
            prepareCmd: [
                'node scripts/write-stamp.mjs',
                "make publish-collector VERSION=${nextRelease.version}",
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
