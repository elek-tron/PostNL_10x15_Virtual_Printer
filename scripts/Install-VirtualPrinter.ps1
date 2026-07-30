param(
    [string]$TargetPrinter,
    [switch]$SelectOnly
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$registryPath = "HKCU:\Software\PostNL10x15"
$virtualPrinterName = "PostNL 10x15"
$resultPath = Join-Path $projectRoot "output\install-result.txt"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

function Show-Message {
    param(
        [string]$Text,
        [string]$Title,
        [System.Windows.Forms.MessageBoxIcon]$Icon
    )

    [System.Windows.Forms.MessageBox]::Show(
        $Text,
        $Title,
        [System.Windows.Forms.MessageBoxButtons]::OK,
        $Icon) | Out-Null
}

$windowsBuild = [Environment]::OSVersion.Version.Build
if ($windowsBuild -lt 26100) {
    Show-Message `
        -Title "PostNL 10x15 - Windows bijwerken" `
        -Text ("Deze virtuele printer vereist Windows 11 24H2 of nieuwer." +
            "`n`nDeze pc heeft Windows-build $windowsBuild." +
            "`nVereiste build: 26100 of hoger." +
            "`n`nWerk Windows bij en start de installer daarna opnieuw.") `
        -Icon Warning
    exit 1
}

function Select-TargetPrinter {
    param(
        [string[]]$PrinterNames,
        [string]$InitialPrinter
    )

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "PostNL 10x15 installeren"
    $form.StartPosition = "CenterScreen"
    $form.ClientSize = New-Object System.Drawing.Size(540, 180)
    $form.FormBorderStyle = "FixedDialog"
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.TopMost = $true

    $label = New-Object System.Windows.Forms.Label
    $label.AutoSize = $true
    $label.Location = New-Object System.Drawing.Point(20, 22)
    $label.Text = "Kies de printer waarop de PostNL-labels moeten worden afgedrukt:"
    $form.Controls.Add($label)

    $comboBox = New-Object System.Windows.Forms.ComboBox
    $comboBox.DropDownStyle = "DropDownList"
    $comboBox.Location = New-Object System.Drawing.Point(20, 55)
    $comboBox.Size = New-Object System.Drawing.Size(500, 28)
    [void]$comboBox.Items.AddRange($PrinterNames)
    $selectedIndex = [Array]::IndexOf($PrinterNames, $InitialPrinter)
    $comboBox.SelectedIndex = if ($selectedIndex -ge 0) { $selectedIndex } else { 0 }
    $form.Controls.Add($comboBox)

    $installButton = New-Object System.Windows.Forms.Button
    $installButton.Text = "Installeren"
    $installButton.Location = New-Object System.Drawing.Point(334, 112)
    $installButton.Size = New-Object System.Drawing.Size(90, 30)
    $installButton.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $form.AcceptButton = $installButton
    $form.Controls.Add($installButton)

    $cancelButton = New-Object System.Windows.Forms.Button
    $cancelButton.Text = "Annuleren"
    $cancelButton.Location = New-Object System.Drawing.Point(430, 112)
    $cancelButton.Size = New-Object System.Drawing.Size(90, 30)
    $cancelButton.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $form.CancelButton = $cancelButton
    $form.Controls.Add($cancelButton)

    try {
        if ($form.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
            return $null
        }

        return [string]$comboBox.SelectedItem
    }
    finally {
        $form.Dispose()
    }
}

function Find-PrinterWithLabelPaperSize {
    param(
        [string[]]$PrinterNames
    )

    $candidates = @(
        foreach ($printerName in $PrinterNames) {
            try {
                $printerSettings =
                    New-Object System.Drawing.Printing.PrinterSettings
                $printerSettings.PrinterName = $printerName
                if (-not $printerSettings.IsValid) {
                    continue
                }

                $paperSize = $printerSettings.DefaultPageSettings.PaperSize
                $widthMm = $paperSize.Width * 25.4 / 100
                $heightMm = $paperSize.Height * 25.4 / 100
                $shortSideMm = [Math]::Min($widthMm, $heightMm)
                $longSideMm = [Math]::Max($widthMm, $heightMm)

                if ($shortSideMm -ge 90 -and
                    $shortSideMm -le 110 -and
                    $longSideMm -ge 140 -and
                    $longSideMm -le 160) {
                    [pscustomobject]@{
                        Name = $printerName
                        Score =
                            [Math]::Abs($shortSideMm - 100) +
                            [Math]::Abs($longSideMm - 150)
                    }
                }
            }
            catch {
                # Sommige printerdrivers geven hun standaardformaat niet door.
                # De printer blijft dan wel gewoon handmatig te kiezen.
            }
        }
    )

    return (
        $candidates |
            Sort-Object Score, Name |
            Select-Object -First 1 -ExpandProperty Name
    )
}

try {
    $printerNames = @(
        Get-Printer |
            Where-Object { $_.Name -notlike "$virtualPrinterName*" } |
            Sort-Object Name |
            Select-Object -ExpandProperty Name
    )
    if ($printerNames.Count -eq 0) {
        throw "Er zijn geen bestaande printers gevonden."
    }

    $savedPrinter = (Get-ItemProperty `
        -Path $registryPath `
        -Name TargetPrinter `
        -ErrorAction SilentlyContinue).TargetPrinter
    $labelPrinter = Find-PrinterWithLabelPaperSize `
        -PrinterNames $printerNames
    $initialPrinter = if ($savedPrinter -in $printerNames) {
        $savedPrinter
    }
    elseif (-not [string]::IsNullOrWhiteSpace($labelPrinter)) {
        $labelPrinter
    }
    elseif ("PDF24" -in $printerNames) {
        "PDF24"
    }
    else {
        $printerNames[0]
    }

    if ([string]::IsNullOrWhiteSpace($TargetPrinter)) {
        $TargetPrinter = Select-TargetPrinter `
            -PrinterNames $printerNames `
            -InitialPrinter $initialPrinter
        if ([string]::IsNullOrWhiteSpace($TargetPrinter)) {
            exit 0
        }
    }
    elseif ($TargetPrinter -notin $printerNames) {
        throw "De gekozen printer '$TargetPrinter' bestaat niet."
    }

    New-Item -Path $registryPath -Force | Out-Null
    Set-ItemProperty `
        -Path $registryPath `
        -Name TargetPrinter `
        -Value $TargetPrinter

    if ($SelectOnly) {
        Write-Host "Doelprinter opgeslagen: $TargetPrinter"
        exit 0
    }

    $elevatedInstaller = Join-Path `
        $PSScriptRoot `
        "Install-VirtualPrinter-Elevated.ps1"
    if (Test-Path -LiteralPath $resultPath) {
        Remove-Item -LiteralPath $resultPath -Force
    }

    $escapedInstaller = $elevatedInstaller.Replace("'", "''")
    $elevatedCommand = "& '$escapedInstaller'"
    $encodedCommand = [Convert]::ToBase64String(
        [Text.Encoding]::Unicode.GetBytes($elevatedCommand))
    $process = Start-Process `
        -FilePath "powershell.exe" `
        -Verb RunAs `
        -PassThru `
        -ArgumentList "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand"

    $progressForm = New-Object System.Windows.Forms.Form
    $progressForm.Text = "PostNL 10x15 installeren"
    $progressForm.StartPosition = "CenterScreen"
    $progressForm.ClientSize = New-Object System.Drawing.Size(460, 115)
    $progressForm.FormBorderStyle = "FixedDialog"
    $progressForm.ControlBox = $false
    $progressForm.TopMost = $true

    $progressLabel = New-Object System.Windows.Forms.Label
    $progressLabel.AutoSize = $false
    $progressLabel.Location = New-Object System.Drawing.Point(20, 18)
    $progressLabel.Size = New-Object System.Drawing.Size(420, 42)
    $progressLabel.Text = "Windows installeert de virtuele printer..."
    $progressForm.Controls.Add($progressLabel)

    $progressBar = New-Object System.Windows.Forms.ProgressBar
    $progressBar.Location = New-Object System.Drawing.Point(20, 70)
    $progressBar.Size = New-Object System.Drawing.Size(420, 18)
    $progressBar.Style = "Marquee"
    $progressForm.Controls.Add($progressBar)

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $progressForm.Show()
    try {
        while (-not $process.HasExited) {
            $progressLabel.Text =
                "Windows installeert de virtuele printer... " +
                "$([int]$stopwatch.Elapsed.TotalSeconds) seconden"
            [System.Windows.Forms.Application]::DoEvents()
            Start-Sleep -Milliseconds 250
        }
        $process.WaitForExit()

        if ($process.ExitCode -eq 0) {
            $progressLabel.Text =
                "De printer wordt aan de Windows-printerlijst toegevoegd..."
            [System.Windows.Forms.Application]::DoEvents()

            $deadline = (Get-Date).AddSeconds(20)
            do {
                $installedPrinter = Get-Printer `
                    -Name $virtualPrinterName `
                    -ErrorAction SilentlyContinue
                if ($installedPrinter) {
                    break
                }
                [System.Windows.Forms.Application]::DoEvents()
                Start-Sleep -Milliseconds 500
            } while ((Get-Date) -lt $deadline)
        }
    }
    finally {
        $stopwatch.Stop()
        $progressForm.Close()
        $progressForm.Dispose()
    }

    if ($process.ExitCode -ne 0 -or -not $installedPrinter) {
        $details = if (Test-Path -LiteralPath $resultPath) {
            Get-Content -Raw -LiteralPath $resultPath
        }
        else {
            "De beheerdersstap is niet gestart of niet met Ja bevestigd."
        }

        throw "De installatie is niet voltooid.`n`n$details"
    }

    Show-Message `
        -Title "PostNL 10x15" `
        -Text "De printer 'PostNL 10x15' is geïnstalleerd.`n`nDoelprinter: $TargetPrinter" `
        -Icon Information
}
catch {
    Show-Message `
        -Title "PostNL 10x15 - installatie niet voltooid" `
        -Text $_.Exception.Message `
        -Icon Error
    exit 1
}
