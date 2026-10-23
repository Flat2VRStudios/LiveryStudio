$ErrorActionPreference='Stop'
$version='2.7.1'
$cache=Join-Path $PSScriptRoot 'artifacts\compiler-compatibility'
$output=Join-Path $PSScriptRoot 'artifacts\workshop-runtime\Compatibility'
New-Item -ItemType Directory -Path $cache,$output -Force | Out-Null
$archive=Join-Path $cache "dxvk-$version.tar.gz"
$archiveHash='D85CE7C79F57ECD765AAA1B9E7007CB875E6FDE9F6D331DF799BCE73D513CE87'
$binaryHash='00ECD422B3B12E9D3B309785F3F19431405389D80EB4A37018EBC63E2D87D900'
$licenseHash='A5CB1A6DED7D2D7E92D550BA28EDD21BE2D1D4044662B399887351023E30CE64'
$url="https://github.com/doitsujin/dxvk/releases/download/v$version/dxvk-$version.tar.gz"
if(-not (Test-Path -LiteralPath $archive)){Invoke-WebRequest -Uri $url -OutFile $archive}
if((Get-FileHash -LiteralPath $archive).Hash -ne $archiveHash){throw 'The pinned compiler compatibility archive did not match its SHA-256.'}
& tar -xf $archive -C $cache
if($LASTEXITCODE -ne 0){throw 'Compiler compatibility extraction failed.'}
$binary=Join-Path $cache "dxvk-$version\x32\d3d9.dll"
if((Get-FileHash -LiteralPath $binary).Hash -ne $binaryHash){throw 'The 32-bit compatibility DLL did not match its SHA-256.'}
$license=Join-Path $cache 'DXVK-LICENSE.txt'
if(-not (Test-Path -LiteralPath $license)){Invoke-WebRequest -Uri "https://raw.githubusercontent.com/doitsujin/dxvk/v$version/LICENSE" -OutFile $license}
if((Get-FileHash -LiteralPath $license).Hash -ne $licenseHash){throw 'The compatibility license did not match its SHA-256.'}
Copy-Item -LiteralPath $binary -Destination (Join-Path $output 'd3d9.dll') -Force
Copy-Item -LiteralPath $license -Destination (Join-Path $output 'DXVK-LICENSE.txt') -Force
[ordered]@{Version=$version;Source=$url;ArchiveSHA256=$archiveHash;BinarySHA256=$binaryHash;LicenseSHA256=$licenseHash;Scope='Original 32-bit Workshop compiler fallback; no Windows or game DLLs are replaced.'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'source.json') -Encoding utf8
Write-Host "Prepared pinned DXVK $version compiler compatibility runtime."
