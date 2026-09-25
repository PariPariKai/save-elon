# Builds the game with the C# compiler that ships with Windows 10 (.NET Framework 4):
#   bin\SaveElon.exe           packed: a tiny loader with the whole game gzip-compressed inside
#   bin\SaveElon-unpacked.exe  the same game without packing (use it if an antivirus dislikes the packed one)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $here 'src'
$out = Join-Path $here 'bin'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force $out | Out-Null

function Gzip($inFile, $outFile) {
    $raw = [IO.File]::ReadAllBytes($inFile)
    $ms = New-Object IO.MemoryStream
    $gz = New-Object IO.Compression.GZipStream($ms, [IO.Compression.CompressionMode]::Compress)
    $gz.Write($raw, 0, $raw.Length)
    $gz.Close()
    [IO.File]::WriteAllBytes($outFile, $ms.ToArray())
}

# the shader is stored compressed inside the game
$frag = Join-Path $env:TEMP 'game.frag.gz'
Gzip "$src\game.frag" $frag

$files = Get-ChildItem "$src\*.cs" | ForEach-Object { $_.FullName }
& $csc /nologo /optimize+ /debug- /target:winexe /platform:anycpu `
    /out:"$out\SaveElon-unpacked.exe" /resource:"$frag,game.frag.gz" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll $files
if ($LASTEXITCODE -ne 0) { throw "compile failed" }

# pack: compress the whole game and wrap it in the loader
$payload = Join-Path $env:TEMP 'saveelon.gz'
Gzip "$out\SaveElon-unpacked.exe" $payload
& $csc /nologo /optimize+ /debug- /target:winexe /platform:anycpu `
    /out:"$out\SaveElon.exe" /resource:"$payload,game" "$here\packer\Stub.cs"
if ($LASTEXITCODE -ne 0) { throw "packer compile failed" }
Remove-Item $frag, $payload

"{0,-24} {1,9:N0} bytes" -f "SaveElon-unpacked.exe", (Get-Item "$out\SaveElon-unpacked.exe").Length
"{0,-24} {1,9:N0} bytes" -f "SaveElon.exe (packed)", (Get-Item "$out\SaveElon.exe").Length
