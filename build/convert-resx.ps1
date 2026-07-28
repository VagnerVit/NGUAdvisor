# Converts WinForms .resx -> classic .resources using .NET Framework (Windows PowerShell 5.1).
# Classic ResourceWriter emits the format Mono reads natively (no System.Resources.Extensions
# dependency), which is required because the DLL is injected into NGU Idle's Mono runtime.
# Run with Windows PowerShell 5.1 (powershell.exe), NOT pwsh/PowerShell 7.

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$proj = Split-Path -Parent $PSScriptRoot   # ...\NGUAdvisor-src
$proj = Join-Path $proj 'NGUAdvisor'

$pairs = @(
    @{ resx = 'SettingsForm.resx';     out = 'SettingsForm.resources' },
    @{ resx = 'SettingsForm.dje.resx'; out = 'SettingsForm.dje.resources' }
)

foreach ($p in $pairs) {
    $resxPath = Join-Path $proj $p.resx
    $outPath  = Join-Path $proj $p.out

    $reader = New-Object System.Resources.ResXResourceReader($resxPath)
    $writer = New-Object System.Resources.ResourceWriter($outPath)
    try {
        $e = $reader.GetEnumerator()
        $count = 0
        while ($e.MoveNext()) {
            $writer.AddResource([string]$e.Key, $e.Value)
            $count++
        }
        $writer.Generate()
        Write-Output ("{0}: {1} resources -> {2}" -f $p.resx, $count, $p.out)
    }
    finally {
        $reader.Close()
        $writer.Close()
    }
}
