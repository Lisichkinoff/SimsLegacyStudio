param([string] $OutputPath = '')

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework 4 C# compiler was not found.'
}

$destination = if ($OutputPath) { [IO.Path]::GetFullPath($OutputPath) } else { Join-Path $root 'SimsLegacyStudio.exe' }
$version = (Get-Content -Raw -LiteralPath (Join-Path $root 'VERSION')).Trim()
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'VERSION must contain a version in MAJOR.MINOR.PATCH format.'
}
$icon = Join-Path $root 'assets\legacy.ico'
$sources = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
$portraits = Get-ChildItem -LiteralPath (Join-Path $root 'assets') -Filter 'portrait-*.jpg' |
    ForEach-Object { '/resource:{0},{1}' -f $_.FullName, $_.Name }

$versionSource = [IO.Path]::GetTempFileName()
try {
    [IO.File]::WriteAllLines($versionSource, @(
        "[assembly: System.Reflection.AssemblyVersion(`"$version.0`")]"
        "[assembly: System.Reflection.AssemblyFileVersion(`"$version.0`")]"
    ))
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/out:$destination" "/win32icon:$icon" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Security.dll $portraits $sources $versionSource
    if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
}
finally {
    Remove-Item -LiteralPath $versionSource
}
Get-Item -LiteralPath $destination | Select-Object FullName, Length
