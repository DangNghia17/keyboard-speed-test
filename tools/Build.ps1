# Build + kiểm thử Keyboard Speed Test bằng trình biên dịch C# có sẵn trong .NET Framework 4.x (không cần cài thêm gì).
#   powershell -ExecutionPolicy Bypass -File tools\Build.ps1            -> build EXE + chạy kiểm thử logic
#   powershell -ExecutionPolicy Bypass -File tools\Build.ps1 -Screens   -> thêm chụp ảnh giao diện vào tests\screenshots
param([switch]$Screens, [switch]$NoTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$src = @(Get-ChildItem (Join-Path $root "src") -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName })
$refs = @('-r:System.dll', '-r:System.Core.dll', '-r:System.Drawing.dll', '-r:System.Windows.Forms.dll')

# 1. Đảm bảo UTF-8 BOM (bắt buộc để Add-Type / csc đọc đúng chuỗi tiếng Việt)
foreach ($f in $src + (Join-Path $root 'KeyboardSpeedTest.ps1') + (Get-ChildItem (Join-Path $root 'tests') -Filter *.cs | ForEach-Object FullName)) {
    $b = [IO.File]::ReadAllBytes($f)
    if (-not ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)) { throw "Thiếu UTF-8 BOM: $f" }
}
Write-Host "[1/4] UTF-8 BOM: OK"

# 2. Build EXE độc lập (nếu EXE đang mở thì chỉ biên dịch kiểm tra, không ghi đè)
$exe = Join-Path $root 'KeyboardSpeedTest.exe'
$locked = $false
if (Test-Path $exe) { try { $fs = [IO.File]::Open($exe, 'Open', 'ReadWrite', 'None'); $fs.Close() } catch { $locked = $true } }
$out = if ($locked) { Join-Path $env:TEMP 'KeyboardSpeedTest.check.exe' } else { $exe }
& $csc -nologo -optimize+ -warnaserror+ -codepage:65001 -target:winexe -main:KeyboardSpeedTest.Program -out:$out @refs @src
if ($LASTEXITCODE -ne 0) { throw 'Build EXE thất bại' }
if ($locked) { Remove-Item $out -ErrorAction SilentlyContinue; Write-Host "[2/4] Build EXE: biên dịch OK, nhưng KeyboardSpeedTest.exe đang mở nên chưa ghi đè - đóng ứng dụng rồi chạy lại Build.ps1" -ForegroundColor Yellow }
else { Write-Host "[2/4] Build EXE: OK -> $exe" }

# 3. Kiểm tra Add-Type (đúng cách launcher KeyboardSpeedTest.ps1 nạp mã nguồn) trong một tiến trình PowerShell 5.1 mới
$check = @"
`$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Path @('$($src -join "','")') -ReferencedAssemblies System.dll, System.Core.dll, System.Drawing.dll, System.Windows.Forms.dll
if (-not ([KeyboardSpeedTest.Program] -as [type])) { exit 2 }
'OK'
"@
$out = powershell.exe -NoProfile -NoLogo -ExecutionPolicy Bypass -Command $check
if ($LASTEXITCODE -ne 0 -or ($out -notcontains 'OK')) { throw "Add-Type thất bại: $out" }
Write-Host "[3/4] Add-Type (Windows PowerShell $($PSVersionTable.PSVersion)): OK"

if ($NoTests) { return }
# 4. Kiểm thử tự động
$binDir = Join-Path $root 'tests\bin'
New-Item -ItemType Directory -Force $binDir | Out-Null
$testExe = Join-Path $binDir 'KeyboardSpeedTest.Tests.exe'
$testSrc = @(Get-ChildItem (Join-Path $root "tests") -Filter *.cs | ForEach-Object { $_.FullName })
& $csc -nologo -warnaserror+ -codepage:65001 -target:exe -main:KeyboardSpeedTest.Tests.TestMain -out:$testExe @refs @src @testSrc
if ($LASTEXITCODE -ne 0) { throw 'Build kiểm thử thất bại' }
[Console]::OutputEncoding = [Text.Encoding]::UTF8
if ($Screens) { & $testExe (Join-Path $root 'tests\screenshots') } else { & $testExe }
if ($LASTEXITCODE -ne 0) { throw 'Có kiểm thử thất bại' }
Write-Host "[4/4] Kiểm thử: OK"
