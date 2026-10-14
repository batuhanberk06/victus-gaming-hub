# Render the code-native Victus chevron at Windows icon sizes. No raster source is modified.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$assetPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\VictusHub.App\Assets'))
$frames = @()
foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
    $visual = New-Object Windows.Media.DrawingVisual
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform([Windows.Media.ScaleTransform]::new(($size/512.0),($size/512.0)))
    $drawing.DrawRoundedRectangle([Windows.Media.BrushConverter]::new().ConvertFromString('#191B22'),$null,[Windows.Rect]::new(0,0,512,512),96,96)
    $drawing.DrawGeometry([Windows.Media.BrushConverter]::new().ConvertFromString('#F1F2F6'),$null,[Windows.Media.Geometry]::Parse('M32 132H180L256 208L332 132H480L256 356Z'))
    $drawing.Pop(); $drawing.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = New-Object IO.MemoryStream
    $encoder.Save($memory)
    $frames += [pscustomobject]@{Size=$size;Bytes=$memory.ToArray()}
    if ($size -eq 256) { [IO.File]::WriteAllBytes((Join-Path $assetPath 'Victus.png'),$memory.ToArray()) }
    $memory.Dispose()
}
$file = [IO.File]::Create((Join-Path $assetPath 'Victus.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
    $offset=6+16*$frames.Count
    foreach ($frame in $frames) {
        $dimension=if ($frame.Size -eq 256) {0} else {$frame.Size}
        $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frame.Bytes.Length);$writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) {$writer.Write([byte[]]$frame.Bytes)}
} finally {$writer.Dispose();$file.Dispose()}
