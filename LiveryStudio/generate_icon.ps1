$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$assets=Join-Path $PSScriptRoot 'App\Assets'
$shapes=@(
    @('M46,115 L67,80 Q77,60 99,60 L167,60 Q188,60 198,82 L219,118 L224,157 Q224,179 205,183 L51,183 Q32,179 32,159 L35,131 Z','body'),
    @('M71,111 L83,84 Q88,76 101,76 L165,76 Q176,76 183,87 L195,111 Z','#152638'),
    @('M72,110 L85,86 Q90,79 100,79 L111,79 L98,110 Z','#3D6471'),
    @('M41,143 L218,143 L218,164 Q218,176 201,176 L56,176 Q40,176 40,161 Z','#DFEEEC'),
    @('M115,117 L131,117 L131,175 L115,175 Z','#D89BE8'),
    @('M137,117 L145,117 L145,175 L137,175 Z','#9957C1'),
    @('M48,148 L89,148 L87,159 L47,158 Z','#111E2D'),
    @('M168,148 L209,148 L210,158 L170,159 Z','#111E2D'),
    @('M51,150 L78,150 L77,154 L51,153 Z','#B8FFF1'),
    @('M180,150 L206,150 L206,153 L181,154 Z','#B8FFF1'),
    @('M99,163 L157,163 L157,170 L99,170 Z','#142333'),
    @('M49,182 L71,182 L71,197 Q71,203 65,203 L55,203 Q49,203 49,197 Z','#0A101C'),
    @('M185,182 L207,182 L207,197 Q207,203 201,203 L191,203 Q185,203 185,197 Z','#0A101C'),
    @('M206,35 L211,48 L224,53 L211,58 L206,72 L201,58 L188,53 L201,48 Z','#D89BE8')
)
function New-IconPng([int]$size) {
    $visual=[Windows.Media.DrawingVisual]::new()
    $dc=$visual.RenderOpen()
    $dc.PushTransform([Windows.Media.ScaleTransform]::new($size/256.0,$size/256.0))
    $brush=[Windows.Media.BrushConverter]::new()
    $dc.DrawRoundedRectangle($brush.ConvertFromString('#132130'),$null,[Windows.Rect]::new(0,0,256,256),48,48)
    $dc.DrawRoundedRectangle($null,[Windows.Media.Pen]::new($brush.ConvertFromString('#294456'),3),[Windows.Rect]::new(6,6,244,244),43,43)
    $dc.DrawEllipse($brush.ConvertFromString('#091320'),$null,[Windows.Point]::new(128,205),88,12)
    foreach($shape in $shapes) {
        if($shape[1] -eq 'body') { $fill=[Windows.Media.LinearGradientBrush]::new([Windows.Media.ColorConverter]::ConvertFromString('#A7FFF0'),[Windows.Media.ColorConverter]::ConvertFromString('#15BAA5'),[Windows.Point]::new(0,0),[Windows.Point]::new(0,1)) } else { $fill=$brush.ConvertFromString($shape[1]) }
        $dc.DrawGeometry($fill,$null,[Windows.Media.Geometry]::Parse($shape[0]))
    }
    $dc.Pop();$dc.Close()
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream=[IO.MemoryStream]::new();$encoder.Save($stream);$data=$stream.ToArray();$stream.Dispose();return ,$data
}
$sizes=@(16,24,32,48,64,128,256)
$pngs=@($sizes | ForEach-Object { ,(New-IconPng $_) })
[IO.File]::WriteAllBytes((Join-Path $assets 'AppIcon.png'),$pngs[-1])
$stream=[IO.File]::Create((Join-Path $assets 'LiveryStudio.ico'));$writer=[IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count);$offset=6+16*$sizes.Count
    for($i=0;$i -lt $sizes.Count;$i++) { $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]};$writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$pngs[$i].Length);$writer.Write([uint32]$offset);$offset+=$pngs[$i].Length }
    foreach($png in $pngs) {$writer.Write([byte[]]$png)}
} finally { $writer.Dispose();$stream.Dispose() }
$paths=($shapes | ForEach-Object { '<path d="'+$_[0]+'" fill="'+$(if($_[1] -eq 'body'){'url(#body)'}else{$_[1]})+'"/>' }) -join "`n"
$svg='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256"><defs><linearGradient id="body" x2="0" y2="1"><stop stop-color="#A7FFF0"/><stop offset="1" stop-color="#15BAA5"/></linearGradient></defs><rect width="256" height="256" rx="48" fill="#132130"/><rect x="6" y="6" width="244" height="244" rx="43" fill="none" stroke="#294456" stroke-width="3"/><ellipse cx="128" cy="205" rx="88" ry="12" fill="#091320"/>'+$paths+'</svg>'
[IO.File]::WriteAllText((Join-Path $assets 'AppIcon.svg'),$svg)
Write-Output 'AppIcon.svg, AppIcon.png and the seven-size Windows icon were generated.'
