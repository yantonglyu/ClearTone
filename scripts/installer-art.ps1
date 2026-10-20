$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$repo=Split-Path $PSScriptRoot -Parent
$out=Join-Path $repo 'installer\assets'
New-Item -ItemType Directory -Path $out -Force|Out-Null
$logo=[Drawing.Image]::FromFile((Join-Path $repo 'src\ClearTone\Assets\ClearTone-logo-master.png'))
try {
  foreach($spec in @(@('welcome.bmp',492,942,400),@('header.bmp',192,192,156))){
    $bitmap=New-Object Drawing.Bitmap($spec[1],$spec[2])
    $g=[Drawing.Graphics]::FromImage($bitmap)
    try {
      $g.Clear([Drawing.ColorTranslator]::FromHtml('#fffaf8'))
      $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
      $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
      $size=[int]$spec[3];$x=[int](($spec[1]-$size)/2);$y=[int](($spec[2]-$size)/2)
      $g.DrawImage($logo,$x,$y,$size,$size)
      $bitmap.Save((Join-Path $out $spec[0]),[Drawing.Imaging.ImageFormat]::Bmp)
    } finally {$g.Dispose();$bitmap.Dispose()}
  }
} finally {$logo.Dispose()}
