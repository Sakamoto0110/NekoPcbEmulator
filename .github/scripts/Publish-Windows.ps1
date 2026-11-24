[CmdletBinding()]
param([string]$OutputRoot = 'artifacts/release')

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$outputPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputRoot))
$version = ([xml](Get-Content (Join-Path $repositoryRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'A stable package version is required.' }
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -cne "v$version") {
    throw "Release tag must match version v$version."
}

$stagingRoot = Join-Path $outputPath ('staging/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
$commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not identify the source commit.' }

foreach ($flavor in @('self-contained', 'framework-dependent')) {
    $selfContained = $flavor -eq 'self-contained'
    $packageRoot = Join-Path $stagingRoot $flavor
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    foreach ($project in @('App', 'TestClient')) {
        $projectFile = Join-Path $repositoryRoot "src/NekoPcbEmulator.$project/NekoPcbEmulator.$project.csproj"
        $binaryRoot = Join-Path $stagingRoot "binaries/$flavor/$project"
        $publishArguments = @(
            'publish', $projectFile, '-c', 'Release', '-r', 'win-x64',
            '--self-contained', $selfContained.ToString().ToLowerInvariant(),
            '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:DebugType=none', '--nologo', '-o', $binaryRoot
        )
        if ($selfContained) { $publishArguments += '-p:EnableCompressionInSingleFile=true' }
        & dotnet @publishArguments
        if ($LASTEXITCODE -ne 0) { throw "Publishing $project ($flavor) failed." }
        $files = @(Get-ChildItem -LiteralPath $binaryRoot -File -Recurse)
        if ($files.Count -ne 1 -or $files[0].Name -cne "NekoPcbEmulator.$project.exe") {
            throw "Expected a single executable for $project ($flavor)."
        }
        Copy-Item -LiteralPath $files[0].FullName -Destination $packageRoot
    }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $packageRoot
    $docsPath = Join-Path $packageRoot 'docs'
    New-Item -ItemType Directory -Path $docsPath | Out-Null
    foreach ($protocol in @('a', 'b')) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot "docs/protocol-$protocol.md") -Destination $docsPath
    }
    Set-Content -LiteralPath (Join-Path $packageRoot 'VERSION.txt') -Value $version -Encoding ascii
    Set-Content -LiteralPath (Join-Path $packageRoot 'BUILD-COMMIT.txt') -Value $commit -Encoding ascii
    $archive = Join-Path $outputPath "NekoPcbEmulator-$version-win-x64-$flavor.zip"
    Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archive -Force
}

$archives = Get-ChildItem -LiteralPath $outputPath -Filter "NekoPcbEmulator-$version-win-x64-*.zip" | Sort-Object Name
$checksums = foreach ($archive in $archives) {
    $hash = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($archive.Name)"
}
Set-Content -LiteralPath (Join-Path $outputPath 'SHA256SUMS.txt') -Value $checksums -Encoding ascii
Set-Content -LiteralPath (Join-Path $outputPath 'VERSION.txt') -Value $version -Encoding ascii
Copy-Item -LiteralPath (Join-Path $repositoryRoot "docs/releases/$version.md") -Destination (Join-Path $outputPath 'RELEASE-NOTES.md')
Write-Output "Published Windows packages for $version at $outputPath"
