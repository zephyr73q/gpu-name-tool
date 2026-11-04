[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1')
& (Join-Path $PSScriptRoot 'test.ps1')
$distDirectory = Join-Path $repoRoot 'dist'
New-Item -ItemType Directory -Path $distDirectory -Force | Out-Null
$zipPath = Join-Path $distDirectory 'gpu-name-tool-windows-x64.zip'
$files = @(
    (Join-Path $repoRoot 'bin\GpuNameTool.exe'),
    (Join-Path $repoRoot 'README.md'),
    (Join-Path $repoRoot 'README.en.md'),
    (Join-Path $repoRoot 'CHANGELOG.md')
)
Compress-Archive -LiteralPath $files -DestinationPath $zipPath -Force
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $distDirectory 'SHA256SUMS.txt'), ($hash + '  gpu-name-tool-windows-x64.zip' + "`n"), [Text.Encoding]::ASCII)
Write-Output ('Packaged: ' + $zipPath)
