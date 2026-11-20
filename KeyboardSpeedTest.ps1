# Keyboard Speed Test
# Launcher: compiles the C# sources in src\ with Add-Type (built into Windows PowerShell 5.1) and starts the app.
# Run it through Chay_KeyboardSpeedTest.bat / Run_KeyboardSpeedTest.bat (Windows Forms needs -STA).
# Optional: -Lang en | -Lang vi skips the language picker.
param([string]$Lang = '')
$ErrorActionPreference = 'Stop'

try {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing

    $srcDir = Join-Path $PSScriptRoot 'src'
    $files = @('Lang.cs', 'Core.cs', 'RawInput.cs', 'UiControls.cs', 'MainForm.cs') | ForEach-Object { Join-Path $srcDir $_ }
    foreach ($f in $files) {
        if (-not (Test-Path -LiteralPath $f)) { throw "Missing source file / Thiếu file mã nguồn: $f`r`nPlease extract the whole folder. / Hãy giải nén đầy đủ thư mục." }
    }

    Add-Type -Path $files -ReferencedAssemblies @(
        'System.dll',
        'System.Core.dll',
        'System.Drawing.dll',
        'System.Windows.Forms.dll'
    )

    if ($Lang) { [KeyboardSpeedTest.Program]::Run([string[]]@('--lang', $Lang)) }
    else { [KeyboardSpeedTest.Program]::Run() }
}
catch {
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
    [System.Windows.Forms.MessageBox]::Show($_.Exception.ToString(), 'Keyboard Speed Test - Error / Lỗi', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 1
}
