$ErrorActionPreference = 'Stop'

# Qt serializes QByteArray to the Windows registry as REG_BINARY containing
# the UTF-16LE encoding of "@ByteArray(" + bytes-as-Latin1 + ")".
# Raw REG_BINARY writes are misread by chiaki's LoadFromSettings, which then
# zeroes the keys -> empty RP-Registkey header -> 0x80108b26 refusal.
# This script rewrites the existing migrated values in correct Qt format.

function Qt-Encode([byte[]]$data) {
    $chars = New-Object char[] ($data.Length + 12)
    "@ByteArray(".ToCharArray().CopyTo($chars, 0)
    for ($i = 0; $i -lt $data.Length; $i++) { $chars[11 + $i] = [char]$data[$i] }
    $chars[11 + $data.Length] = ')'
    return [System.Text.Encoding]::Unicode.GetBytes((-join $chars))
}

function Qt-Decode([byte[]]$raw) {
    try {
        $str = [System.Text.Encoding]::Unicode.GetString($raw)
        if ($str.StartsWith("@ByteArray(") -and $str.EndsWith(")")) {
            $inner = $str.Substring(11, $str.Length - 12)
            $bytes = New-Object byte[] $inner.Length
            for ($i = 0; $i -lt $inner.Length; $i++) { $bytes[$i] = [byte][char]$inner[$i] }
            return $bytes
        }
    } catch {}
    return $raw
}

foreach ($store in @('HKCU:\Software\Vectis\Vectis', 'HKCU:\Software\Chiaki\Chiaki')) {
    $hostsKey = "$store\registered_hosts"
    if (-not (Test-Path $hostsKey)) { "skip $store (missing)"; continue }
    $size = (Get-ItemProperty $hostsKey).size
    for ($i = 1; $i -le $size; $i++) {
        $hk = "$hostsKey\$i"
        if (-not (Test-Path $hk)) { continue }
        $props = Get-ItemProperty $hk
        foreach ($name in @('server_mac', 'rp_regist_key', 'rp_key')) {
            $v = $props.$name
            if ($v -is [byte[]]) {
                $decoded = Qt-Decode $v
                # if it already decodes as @ByteArray utf16, decoded length differs sensibly; check raw form
                $isQt = $false
                try {
                    $s = [System.Text.Encoding]::Unicode.GetString($v)
                    if ($s.StartsWith("@ByteArray(")) { $isQt = $true }
                } catch {}
                if (-not $isQt) {
                    $enc = Qt-Encode $v
                    Set-ItemProperty -Path $hk -Name $name -Value $enc -Type Binary
                    "$store host ${i}: $name rewritten raw($($v.Length)) -> qt($($enc.Length))"
                } else {
                    "$store host ${i}: $name already qt format"
                }
            }
        }
    }
    # settings-level binary values
    $sk = "$store\settings"
    if (Test-Path $sk) {
        $props = Get-ItemProperty $sk
        foreach ($name in @('auto_connect_mac')) {
            $v = $props.$name
            if ($v -is [byte[]]) {
                $isQt = $false
                try {
                    $s = [System.Text.Encoding]::Unicode.GetString($v)
                    if ($s.StartsWith("@ByteArray(")) { $isQt = $true }
                } catch {}
                if (-not $isQt) {
                    $enc = Qt-Encode $v
                    Set-ItemProperty -Path $sk -Name $name -Value $enc -Type Binary
                    "$store settings: $name rewritten raw($($v.Length)) -> qt($($enc.Length))"
                } else {
                    "$store settings: $name already qt format"
                }
            }
        }
    }
}
"done"
