[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw 'The 64-bit .NET Framework C# compiler was not found. Windows x64 with .NET Framework 4.x is required.'
}
$outputDirectory = Join-Path $repoRoot 'bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$compilerArgs = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001',
    ('/out:' + (Join-Path $outputDirectory 'GpuNameTool.exe')),
    ('/win32manifest:' + (Join-Path $repoRoot 'src\app.manifest')),
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    '/reference:System.Core.dll', '/reference:System.Web.Extensions.dll',
    (Join-Path $repoRoot 'src\Core.cs'), (Join-Path $repoRoot 'src\App.cs')
)
& $compilerPath @compilerArgs
if ($LASTEXITCODE -ne 0) { throw ('Build failed with exit code ' + $LASTEXITCODE) }
Write-Output ('Built: ' + (Join-Path $outputDirectory 'GpuNameTool.exe'))
