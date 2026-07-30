param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Pdf
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$worker = Join-Path $projectRoot "src\PostNL10x15.Worker\bin\Release\net8.0-windows\PostNL10x15.exe"

if (-not (Test-Path -LiteralPath $worker)) {
    dotnet build (Join-Path $projectRoot "src\PostNL10x15.Worker\PostNL10x15.Worker.csproj") --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "Bouwen is mislukt."
    }
}

& $worker print $Pdf --printer "PDF24"
exit $LASTEXITCODE

