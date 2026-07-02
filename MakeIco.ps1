$pngPath = "C:\FluidDecks\Icon.png"
$icoPath = "C:\FluidDecks\Icon.ico"

$pngBytes = [System.IO.File]::ReadAllBytes($pngPath)

$icoHeader = New-Object byte[] 22
$icoHeader[0] = 0; $icoHeader[1] = 0 # Reserved
$icoHeader[2] = 1; $icoHeader[3] = 0 # Type = ICO
$icoHeader[4] = 1; $icoHeader[5] = 0 # Number of images
$icoHeader[6] = 0; $icoHeader[7] = 0 # Width, Height (0 = 256)
$icoHeader[8] = 0; $icoHeader[9] = 0 # Color count, reserved
$icoHeader[10] = 1; $icoHeader[11] = 0 # Color planes
$icoHeader[12] = 32; $icoHeader[13] = 0 # BPP

$len = $pngBytes.Length
$icoHeader[14] = [byte]($len -band 255)
$icoHeader[15] = [byte](($len -shr 8) -band 255)
$icoHeader[16] = [byte](($len -shr 16) -band 255)
$icoHeader[17] = [byte](($len -shr 24) -band 255)

$icoHeader[18] = 22; $icoHeader[19] = 0; $icoHeader[20] = 0; $icoHeader[21] = 0 # Offset

$file = [System.IO.File]::Create($icoPath)
$file.Write($icoHeader, 0, 22)
$file.Write($pngBytes, 0, $pngBytes.Length)
$file.Close()

Write-Host "Created $icoPath successfully."
