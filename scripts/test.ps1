[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'The 64-bit .NET Framework C# compiler was not found.' }
$artifactDirectory = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$testExe = Join-Path $artifactDirectory 'GpuNameTool.Tests.exe'
$compilerArgs = @(
    '/nologo', '/target:exe', '/platform:x64', '/codepage:65001', ('/out:' + $testExe),
    '/reference:System.Core.dll', '/reference:System.Web.Extensions.dll',
    (Join-Path $repoRoot 'src\Core.cs'), (Join-Path $repoRoot 'tests\Tests.cs')
)
& $compilerPath @compilerArgs
if ($LASTEXITCODE -ne 0) { throw ('Test compilation failed with exit code ' + $LASTEXITCODE) }
$testDataDirectory = Join-Path $artifactDirectory ('test-data-' + [Guid]::NewGuid().ToString('N'))
& $testExe $testDataDirectory | Tee-Object -FilePath (Join-Path $artifactDirectory 'test-results.txt')
if ($LASTEXITCODE -ne 0) { throw ('Tests failed with exit code ' + $LASTEXITCODE) }
