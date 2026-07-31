$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultPath = Join-Path $projectRoot "output\install-result.txt"
$certificatePath = Join-Path `
    $projectRoot `
    "certificates\PostNL10x15-Development.cer"
$packageName = "PostNL10x15.VirtualPrinter"

function Write-Result {
    param([string]$Text)

    New-Item `
        -ItemType Directory `
        -Force `
        -Path (Split-Path -Parent $resultPath) |
        Out-Null
    $Text | Set-Content -LiteralPath $resultPath
}

function Test-DesktopRuntime {
    $runtimeRoot = Join-Path `
        $env:ProgramFiles `
        "dotnet\shared\Microsoft.WindowsDesktop.App"
    if (-not (Test-Path -LiteralPath $runtimeRoot)) {
        return $false
    }

    return [bool](Get-ChildItem `
        -LiteralPath $runtimeRoot `
        -Directory `
        -Filter "8.*" `
        -ErrorAction SilentlyContinue |
        Select-Object -First 1)
}

function Install-AppxDependencyIfNeeded {
    param(
        [string]$Name,
        [version]$MinimumVersion,
        [string]$FileName
    )

    $installedDependency = Get-AppxPackage `
        -Name $Name `
        -ErrorAction SilentlyContinue |
        Where-Object { $_.Architecture -in "X64", "Neutral" } |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if ($installedDependency -and
        [version]$installedDependency.Version -ge $MinimumVersion) {
        return
    }

    $dependencyPath = Join-Path `
        $projectRoot `
        "AppPackages\Dependencies\x64\$FileName"
    if (-not (Test-Path -LiteralPath $dependencyPath)) {
        throw "Het meegeleverde Windows-onderdeel '$FileName' ontbreekt."
    }

    Add-AppxPackage `
        -Path $dependencyPath `
        -ForceApplicationShutdown

    $installedDependency = Get-AppxPackage `
        -Name $Name `
        -ErrorAction SilentlyContinue |
        Where-Object { $_.Architecture -in "X64", "Neutral" } |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if (-not $installedDependency -or
        [version]$installedDependency.Version -lt $MinimumVersion) {
        throw "Het vereiste Windows-onderdeel '$Name' is niet geïnstalleerd."
    }
}

try {
    if (-not (Test-DesktopRuntime)) {
        $runtimeInstaller = Get-ChildItem `
            -LiteralPath (Join-Path $projectRoot "Runtime") `
            -File `
            -Filter "windowsdesktop-runtime-8.*-win-x64.exe" `
            -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if (-not $runtimeInstaller) {
            throw (
                "Het meegeleverde .NET 8 Desktop Runtime-installatiebestand " +
                "ontbreekt.")
        }

        $runtimeProcess = Start-Process `
            -FilePath $runtimeInstaller.FullName `
            -ArgumentList "/install /quiet /norestart" `
            -PassThru `
            -Wait
        if ($runtimeProcess.ExitCode -notin 0, 3010) {
            throw (
                "De installatie van .NET 8 Desktop Runtime is mislukt " +
                "met foutcode $($runtimeProcess.ExitCode).")
        }
        if (-not (Test-DesktopRuntime)) {
            throw ".NET 8 Desktop Runtime is na de installatie niet gevonden."
        }
    }

    $package = Get-ChildItem `
        -LiteralPath (Join-Path $projectRoot "AppPackages") `
        -File `
        -Recurse `
        -Filter "PostNL10x15.VirtualPrinter_*_x64.msix" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $package) {
        throw "Het PostNL 10x15-printerpakket is niet gevonden."
    }

    $certificate =
        [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $certificatePath)
    try {
        foreach ($storeName in @(
                [System.Security.Cryptography.X509Certificates.StoreName]::Root,
                [System.Security.Cryptography.X509Certificates.StoreName]::TrustedPeople,
                [System.Security.Cryptography.X509Certificates.StoreName]::TrustedPublisher)) {
            $store =
                [System.Security.Cryptography.X509Certificates.X509Store]::new(
                    $storeName,
                    [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
            try {
                $store.Open(
                    [System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
                if (-not ($store.Certificates |
                        Where-Object Thumbprint -eq $certificate.Thumbprint)) {
                    $store.Add($certificate)
                }
                if (-not ($store.Certificates |
                        Where-Object Thumbprint -eq $certificate.Thumbprint)) {
                    throw (
                        "Het installatiecertificaat kon niet worden " +
                        "vertrouwd in $storeName.")
                }
            }
            finally {
                $store.Close()
            }
        }
    }
    finally {
        $certificate.Dispose()
    }

    Install-AppxDependencyIfNeeded `
        -Name "Microsoft.VCLibs.140.00" `
        -MinimumVersion "14.0.33519.0" `
        -FileName "Microsoft.VCLibs.x64.14.00.appx"
    Install-AppxDependencyIfNeeded `
        -Name "Microsoft.NET.CoreRuntime.2.2" `
        -MinimumVersion "2.2.31331.1" `
        -FileName "Microsoft.NET.CoreRuntime.2.2.appx"
    Install-AppxDependencyIfNeeded `
        -Name "Microsoft.NET.CoreFramework.Debug.2.2" `
        -MinimumVersion "2.2.31327.1" `
        -FileName "Microsoft.NET.CoreFramework.Debug.2.2.appx"

    $installed = Get-AppxPackage `
        -Name $packageName `
        -ErrorAction SilentlyContinue
    if ($installed) {
        Remove-AppxPackage `
            -Package $installed.PackageFullName `
            -Confirm:$false
        Start-Sleep -Seconds 2
    }

    Add-AppxPackage `
        -Path $package.FullName `
        -ForceApplicationShutdown

    Write-Result "OK $(Get-Date -Format O)"
}
catch {
    $errorRecord = $_
    $errorText = $errorRecord | Out-String
    $activityMatch = [regex]::Match(
        $errorText,
        "(?i)(?:ActivityId\]\s*|ActivityID\s+)" +
        "([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-" +
        "[0-9a-f]{4}-[0-9a-f]{12})")
    $appxLogMessage = ""
    if ($activityMatch.Success) {
        $activityId = [guid]$activityMatch.Groups[1].Value
        $appxLogPath = Join-Path `
            (Split-Path -Parent $resultPath) `
            "appx-installatielog.txt"
        try {
            Get-AppPackageLog -ActivityID $activityId |
                Format-List Time, Id, Message |
                Out-File -LiteralPath $appxLogPath -Width 300
            $appxLogMessage =
                [Environment]::NewLine +
                "Windows-detailrapport: $appxLogPath"
        }
        catch {
            $appxLogMessage =
                [Environment]::NewLine +
                "Het Windows-detailrapport kon niet worden gelezen."
        }
    }

    Write-Result (
        "FOUT " + (Get-Date -Format O) +
        [Environment]::NewLine + $errorRecord.Exception.Message +
        [Environment]::NewLine + $errorRecord.FullyQualifiedErrorId +
        [Environment]::NewLine + $errorRecord.ScriptStackTrace +
        $appxLogMessage)
    throw
}
