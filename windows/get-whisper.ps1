# Fetch prebuilt whisper.cpp for Windows (BLAS build preferred: much faster on CPU), else build from source.
$ErrorActionPreference = "Stop"
$dest = Join-Path $PSScriptRoot "publish\whisper"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$tmp = Join-Path $env:RUNNER_TEMP "whisper"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

$tag = gh release view --repo ggml-org/whisper.cpp --json tagName --jq .tagName
$assets = gh release view --repo ggml-org/whisper.cpp --json assets --jq ".assets[].name"
Write-Host "whisper.cpp $tag assets:"; $assets | ForEach-Object { Write-Host "  $_" }

$ok = $false
foreach ($pattern in @("whisper-blas-bin-x64.zip", "whisper-bin-x64.zip")) {
  if ($assets -contains $pattern) {
    Write-Host "Downloading $pattern"
    gh release download $tag --repo ggml-org/whisper.cpp --pattern $pattern --dir $tmp --clobber
    Expand-Archive -Path (Join-Path $tmp $pattern) -DestinationPath (Join-Path $tmp "x") -Force
    $server = Get-ChildItem -Path (Join-Path $tmp "x") -Recurse -Filter "whisper-server.exe" | Select-Object -First 1
    if ($server) {
      Copy-Item (Join-Path $server.DirectoryName "*.dll") $dest -ErrorAction SilentlyContinue
      Copy-Item $server.FullName $dest
      $ok = $true
      break
    }
  }
}

if (-not $ok) {
  Write-Host "No prebuilt whisper-server.exe — building from source"
  git clone --depth 1 --branch $tag https://github.com/ggml-org/whisper.cpp (Join-Path $tmp "src")
  cmake -S (Join-Path $tmp "src") -B (Join-Path $tmp "build") -DBUILD_SHARED_LIBS=OFF -DGGML_NATIVE=OFF -DGGML_AVX2=ON -DWHISPER_BUILD_TESTS=OFF -DWHISPER_SDL2=OFF
  cmake --build (Join-Path $tmp "build") --config Release -j 4 --target whisper-server
  $server = Get-ChildItem -Path (Join-Path $tmp "build") -Recurse -Filter "whisper-server.exe" | Select-Object -First 1
  Copy-Item (Join-Path $server.DirectoryName "*.dll") $dest -ErrorAction SilentlyContinue
  Copy-Item $server.FullName $dest
}
Write-Host "Bundled:"; Get-ChildItem $dest | ForEach-Object { Write-Host "  $($_.Name)  $([math]::Round($_.Length/1MB,1)) MB" }
