param(
    [string]$PythonMkdocs = ".\\.venv\\Scripts\\mkdocs.exe",
    [string]$OutputDir = ".\\artifacts",
    [string]$ArchiveName = "hobiary-logpipe-docs.zip"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PythonMkdocs)) {
    throw "MkDocs executable not found at '$PythonMkdocs'."
}

& $PythonMkdocs build --strict

if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputDir | Out-Null

$archivePath = Join-Path $OutputDir $ArchiveName

Compress-Archive -Path ".\site\*" -DestinationPath $archivePath -Force

Write-Host "Created docs archive: $archivePath"