# Builds SoftBright.exe with the .NET Framework compiler that ships with Windows.
# No SDK or Visual Studio needed. Run:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src\Program.cs'
$out  = Join-Path $root 'SoftBright.exe'
$ico  = Join-Path $root 'app.ico'

Add-Type -AssemblyName System.Drawing

# --- generate app icon (amber rounded square with a sun glyph) ---
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 56; $d = $r * 2
$path.AddArc(8, 8, $d, $d, 180, 90); $path.AddArc(248 - $d, 8, $d, $d, 270, 90)
$path.AddArc(248 - $d, 248 - $d, $d, $d, 0, 90); $path.AddArc(8, 248 - $d, $d, $d, 90, 90); $path.CloseFigure()
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 170, 30))
$g.FillPath($brush, $path)
$g.FillEllipse([System.Drawing.Brushes]::White, 88, 88, 80, 80)
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 14
$pen.StartCap = 'Round'; $pen.EndCap = 'Round'
for ($i = 0; $i -lt 8; $i++) {
    $a = $i * [Math]::PI / 4
    $x1 = 128 + 58 * [Math]::Cos($a); $y1 = 128 + 58 * [Math]::Sin($a)
    $x2 = 128 + 84 * [Math]::Cos($a); $y2 = 128 + 84 * [Math]::Sin($a)
    $g.DrawLine($pen, [float]$x1, [float]$y1, [float]$x2, [float]$y2)
}
$g.Dispose()
$hicon = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hicon)
$fs = [System.IO.File]::Create($ico)
$icon.Save($fs); $fs.Close()
$bmp.Dispose()

# --- compile ---
if (Test-Path $out) { Remove-Item $out -Force }
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateExecutable = $true
$cp.OutputAssembly = $out
$cp.GenerateInMemory = $false
$cp.ReferencedAssemblies.AddRange([string[]]@('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll'))
$cp.CompilerOptions = "/target:winexe /optimize+ /win32icon:`"$ico`""
$code = [System.IO.File]::ReadAllText($src, [System.Text.Encoding]::UTF8)
Add-Type -TypeDefinition $code -CompilerParameters $cp
Write-Host "OK -> $out"
