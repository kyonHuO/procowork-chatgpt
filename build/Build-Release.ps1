[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$build = Join-Path $repo 'artifacts/release'
$source = Join-Path $build 'sign-in-with-chatgpt-devkit'
$sdk = Join-Path $build 'sdk'
$upstream = '0a36fefeb913055c8c7a1a29b63d82b96b2e841a'

function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable exited with code $LASTEXITCODE." }
}

New-Item -ItemType Directory -Force $build, $sdk | Out-Null
if (!(Test-Path (Join-Path $source '.git'))) {
    Invoke-Checked 'git' @('clone', '--filter=blob:none', 'https://github.com/openai/sign-in-with-chatgpt-devkit.git', $source)
}
Invoke-Checked 'git' @('-C', $source, 'checkout', '--detach', $upstream)

$indexFile = Join-Path $source 'packages/local/src/index.ts'
$typeFile = Join-Path $source 'packages/local/src/types.ts'
# Start from pinned sources on every run so the patch can be reapplied safely.
Invoke-Checked 'git' @('-C', $source, 'restore', '--source=HEAD', '--', 'packages/local/src/index.ts', 'packages/local/src/types.ts')
$index = [IO.File]::ReadAllText($indexFile)
$types = [IO.File]::ReadAllText($typeFile)
$indexAnchor = '    async streamResponse(options) {'
$typeAnchor = '  streamResponse(options: StreamResponseOptions)'
if (!$index.Contains($indexAnchor) -or !$types.Contains($typeAnchor)) {
    throw 'The pinned Devkit no longer matches the ProCowork patch.'
}

$notice = '// Modified by ProCowork. Adds a trusted local OAuth handoff for Codex app-server.'
$indexPatch = '    withPlanAccess(operation, signal) { return authenticated(operation, signal); },'
$typePatch = '  withPlanAccess<T>(operation: (accessToken: string, signal: AbortSignal) => Promise<T>, signal?: AbortSignal): Promise<T>;'
$newline = [Environment]::NewLine
[IO.File]::WriteAllText($indexFile, $notice + $newline + $index.Replace($indexAnchor, $indexPatch + $newline + $indexAnchor))
[IO.File]::WriteAllText($typeFile, $notice + $newline + $types.Replace($typeAnchor, $typePatch + $newline + $typeAnchor))

Push-Location $source
try {
    Invoke-Checked 'npm.cmd' @('ci', '--ignore-scripts', '--workspace', '@siwc/local', '--include-workspace-root')
    Invoke-Checked 'npm.cmd' @('run', 'build', '-w', '@siwc/local')
    Invoke-Checked 'node.exe' @('--test', 'packages/local/test/*.test.mjs')
} finally { Pop-Location }

Copy-Item (Join-Path $PSScriptRoot 'devkit-runtime/package*.json') $sdk -Force
Copy-Item (Join-Path $source 'packages/local/dist') $sdk -Recurse -Force
Copy-Item (Join-Path $source 'LICENSE'), (Join-Path $source 'THIRD_PARTY_NOTICES.md') (Join-Path $sdk 'dist') -Force
$noticeDir = Join-Path $sdk 'dist/docs'
New-Item -ItemType Directory -Force $noticeDir | Out-Null
Copy-Item (Join-Path $source 'docs/dependency-inventory.json') $noticeDir -Force

Push-Location $sdk
try {
    Invoke-Checked 'npm.cmd' @('ci', '--omit=dev', '--omit=optional', '--ignore-scripts')
    Invoke-Checked 'npm.cmd' @('audit', '--omit=dev', '--omit=optional', '--audit-level=high')
} finally { Pop-Location }

Push-Location $repo
try {
    $version = & dotnet --version
    if ($LASTEXITCODE -ne 0 -or $version -notlike '8.*') { throw 'Install the .NET 8 SDK.' }
    $sdkEntry = & dotnet --list-sdks | Where-Object { $_ -like "$version *" } | Select-Object -First 1
    if ($sdkEntry -notmatch '\[([^\]]+)\]') { throw 'Could not locate the .NET 8 SDK.' }
    $env:MSBuildSDKsPath = Join-Path $Matches[1] "$version/Sdks"
    $env:MSBuildEnableWorkloadResolver = 'false'

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\amd64\MSBuild.exe' | Select-Object -First 1
    if (!$msbuild) { throw 'Install Visual Studio or Visual Studio Build Tools with MSBuild.' }
    $disabledInstallPath = Join-Path $build 'registration-disabled'
    $output = Join-Path $build 'addin'
    Invoke-Checked $msbuild @(
        'src/ArcGISClaude/ArcGISClaude.csproj', '-restore', '-t:Rebuild', '-v:minimal',
        '-p:Configuration=Release', '-p:Platform=x64', '-p:DebugType=None', "-p:ChatGptSdkDir=$sdk",
        "-p:ArcGISFolder=$disabledInstallPath", '-p:SkipLocalDeploy=true', "-p:OutDir=$output/"
    )
} finally { Pop-Location }

$package = Join-Path $build 'ProCowork-ChatGPT-0.1.15.esriAddinX'
Copy-Item (Join-Path $output 'ArcGISClaude.esriAddinX') $package -Force
Get-FileHash -LiteralPath $package -Algorithm SHA256
