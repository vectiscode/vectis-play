param(
    [Parameter(Mandatory=$true)]
    [string]$ConsoleIdHex
)

$ErrorActionPreference = 'Stop'

# chiaki discovery derives the host "MAC" from the discovery host_id, which on
# PS5 is the console id (D4F7D5F3A713), NOT the network MAC. server_mac must
# match THAT or auto-connect and wakeup lookups never match.

function Qt-Encode([byte[]]$data) {
    $chars = New-Object char[] ($data.Length + 12)
    "@ByteArray(".ToCharArray().CopyTo($chars, 0)
    for ($i = 0; $i -lt $data.Length; $i++) { $chars[11 + $i] = [char]$data[$i] }
    $chars[11 + $data.Length] = ')'
    return [System.Text.Encoding]::Unicode.GetBytes((-join $chars))
}

$clean = $ConsoleIdHex -replace "[^0-9A-Fa-f]", ""
if ($clean.Length -ne 12) { throw "ConsoleIdHex must be exactly 12 hex characters" }
$consoleIdMac = New-Object byte[] 6
for ($i = 0; $i -lt 6; $i++) { $consoleIdMac[$i] = [Convert]::ToByte($clean.Substring($i * 2, 2), 16) }
$enc = Qt-Encode $consoleIdMac

foreach ($store in @('HKCU:\Software\Vectis\Vectis', 'HKCU:\Software\Chiaki\Chiaki')) {
    $hostsKey = "$store\registered_hosts"
    if (-not (Test-Path $hostsKey)) { continue }
    $size = (Get-ItemProperty $hostsKey).size
    for ($i = 1; $i -le $size; $i++) {
        $hk = "$hostsKey\$i"
        if (-not (Test-Path $hk)) { continue }
        Set-ItemProperty -Path $hk -Name 'server_mac' -Value $enc -Type Binary
        "$store host ${i}: server_mac -> console-id $clean (qt format)"
    }
    $sk = "$store\settings"
    if (Test-Path $sk) {
        Set-ItemProperty -Path $sk -Name 'auto_connect_mac' -Value $enc -Type Binary
        "$store settings: auto_connect_mac -> console-id (qt format)"
    }
}
"done"
