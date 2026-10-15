# Builds the shareable package: dist\BookDepthIQ-DOM-<version>-win-x64.zip
#   BookDepthIQ-DOM.exe            one self-contained file (no .NET install needed on the user's PC)
#   NinjaTrader\BookDepthIQBridge.cs  the AddOn the user imports into NinjaTrader 8
#   INSTALL.txt                    the steps
#   LICENSE.txt, DISCLAIMER.txt    MIT license and the risk / third-party notice
# Usage:  powershell -ExecutionPolicy Bypass -File tools\package.ps1 [-Version 1.0.0]
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw "Version must be a semantic version, not a path" }
$out = Join-Path $root "dist"
$stage = Join-Path $out "BookDepthIQ-DOM-$Version"
$stage = [System.IO.Path]::GetFullPath($stage)
$packageBoundary = [System.IO.Path]::GetFullPath($out) + [System.IO.Path]::DirectorySeparatorChar
if (-not $stage.StartsWith($packageBoundary, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Package stage escapes dist" }
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\NinjaTrader" | Out-Null

dotnet test (Join-Path $root "BookDepthIQ-NT.sln") -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "tests failed - not packaging" }

dotnet publish (Join-Path $root "src\Dom") -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:Version=$Version -p:DebugType=none -o "$stage" -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Copy-Item (Join-Path $root "nt8\AddOns\BookDepthIQBridge.cs") "$stage\NinjaTrader\"
Copy-Item (Join-Path $root "nt8\AddOns\BookDepthIQChartBridge.cs") "$stage\NinjaTrader\"
Copy-Item (Join-Path $root "LICENSE") "$stage\LICENSE.txt"
Copy-Item (Join-Path $root "DISCLAIMER.md") "$stage\DISCLAIMER.txt"
@"
BookDepthIQ DOM $Version - install
====================================

1. NinjaTrader AddOn (replace when upgrading to $Version)
   - Close NinjaTrader.
   - Copy BOTH NinjaTrader\BookDepthIQBridge.cs and NinjaTrader\BookDepthIQChartBridge.cs into
       Documents\NinjaTrader 8\bin\Custom\AddOns\
   - Start NinjaTrader, open New > NinjaScript Editor and press F5 to compile.
   - The bridge listens on 127.0.0.1:36971 (this PC only).
   - The separate read-only chart bridge listens on 127.0.0.1:36974 (36972 belongs to Bubbles).

2. The DOM
   - Unzip anywhere and run BookDepthIQ-DOM.exe (no install, no .NET needed).
   - Windows may show "Windows protected your PC" (the exe is not code-signed):
     More info > Run anyway.
   - Pick your account (Sim101 to test) and instrument, e.g. NQ 12-26, then GO.
   - CHARTS shows two optional overview charts. Maximize the window if they remain hidden.
     Drag the chart/DOM divider and the divider between charts to resize; sizing is saved.
     Chart menus contain timeframe-independent lookback, studies and PAX-style OR preferences.
     PAX is disabled on 1h, 4h and Day. No chart can submit orders.
   - This two-chart development build has isolated tests, not native provider/parity or latency certification.

3. Before trading live
   - Every order is your click. Set your prop firm's numbers in SETTINGS > Account.
   - Test on Sim101 first.

Settings are saved in %APPDATA%\BookDepthIQ-NT\.

Trading futures involves substantial risk of loss. This is a tool, not financial advice, and it
comes with no warranty (MIT license, LICENSE.txt). Read DISCLAIMER.txt.
https://github.com/autopilotaitech/BookDepthIQ-NT
"@ | Set-Content "$stage\INSTALL.txt" -Encoding utf8

$zip = Join-Path $out "BookDepthIQ-DOM-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
"{0}  ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)
