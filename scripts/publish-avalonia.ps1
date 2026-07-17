param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\publish\avalonia-win-x64")
)

$ErrorActionPreference = "Stop"

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectFile = Join-Path $projectRoot "src\CpuAffinityManager.Avalonia\CpuAffinityManager.Avalonia.csproj"
$outputDirectory = [System.IO.Path]::GetFullPath($OutputPath)

dotnet publish $projectFile -c Release -r win-x64 --self-contained true -p:UseSharedCompilation=false -o $outputDirectory
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

New-Item -ItemType Directory -Force -Path (Join-Path $outputDirectory "config") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $outputDirectory "docs") | Out-Null
Copy-Item (Join-Path $projectRoot "config\default-rules.json") (Join-Path $outputDirectory "config\default-rules.json") -Force
Copy-Item (Join-Path $projectRoot "docs\ai-guide.md") (Join-Path $outputDirectory "docs\ai-guide.md") -Force
Copy-Item (Join-Path $projectRoot "README.md") (Join-Path $outputDirectory "README.md") -Force

Write-Host "Avalonia release ready: $outputDirectory"
