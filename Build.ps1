$ErrorActionPreference = 'Stop'

$framework = Join-Path $env:windir 'Microsoft.NET/Framework64/v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:windir 'Microsoft.NET/Framework/v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Build requires the Windows .NET Framework 4.x C# compiler.'
}

$source = Join-Path $PSScriptRoot 'src'
$glass = Join-Path $source 'Glass'
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null

$exe = Join-Path $dist 'ArcGlow.exe'
$icon = Join-Path $glass 'arcglow.ico'
$manifest = Join-Path $glass 'app.manifest'
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', '/codepage:65001',
    "/out:$exe", "/win32manifest:$manifest", "/win32icon:$icon", "/resource:$icon,clicker.ico")
$references = @('System.Xaml.dll', 'WPF/WindowsBase.dll', 'WPF/PresentationCore.dll', 'WPF/PresentationFramework.dll',
    'WPF/UIAutomationClient.dll', 'WPF/UIAutomationTypes.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Web.Extensions.dll')
$arguments += $references | ForEach-Object { "/reference:$(Join-Path $framework $_)" }
$arguments += @('click.wav', 'key.wav') | ForEach-Object {
    $sound = Join-Path $glass "Audio/$_"
    "/resource:$sound,ArcGlow.Audio.$_"
}
$arguments += @((Join-Path $source 'NativeMouse.cs'), (Join-Path $source 'NativeInput.cs'))
$arguments += Get-ChildItem -LiteralPath $glass -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName

& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
Get-Item -LiteralPath $exe
