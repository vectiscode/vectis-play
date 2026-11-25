# PS5 Remote Play registration client - port of chiaki regist.c/rpcrypt.c
# (via PS5-Suite's C# port, protocol facts for interoperability)
import socket, sys, os, hmac, hashlib, base64, json, tempfile
from Crypto.Cipher import AES

BRIGHT_BASE = bytes([
    0xD8, 0x60, 0xE1, 0x46, 0xBD, 0xB0, 0xBD, 0x94,
    0xB4, 0x60, 0x07, 0x0B, 0xB1, 0x4F, 0xE7, 0x23])
AERO_K = bytes([
    0xA8, 0x37, 0x61, 0xB5, 0x27, 0xFC, 0x07, 0x6E,
    0x62, 0x43, 0x3A, 0xA7, 0x01, 0x95, 0x8A, 0x85])
HMAC_KEY_PS5 = bytes([
    0x46, 0x46, 0x87, 0xB3, 0x49, 0xCA, 0x8C, 0xE8,
    0x59, 0xC5, 0x27, 0x0F, 0x5D, 0x7A, 0x69, 0xD6])
CLIENT_TYPE = "dabfa2ec873de5839bee8d3f4c0239c4282c07c25c6077a2931afcf0adc0d34f"

REASONS = {
    0x80108b02: "Invalid PSN ID",
    0x80108bff: "PIN invalid or expired",
    0x80108c00: "PIN format error",
    0x80108c01: "console not ready (link screen closed)",
}

def make_iv(ambassador, counter=0):
    buf = ambassador + counter.to_bytes(8, "big")
    return hmac.new(HMAC_KEY_PS5, buf, hashlib.sha256).digest()[:16]

def aes_cfb(key, iv, data):
    c = AES.new(key, AES.MODE_CFB, iv=iv, segment_size=128)
    return c.encrypt(data)

def aes_cfb_decrypt(key, iv, data):
    c = AES.new(key, AES.MODE_CFB, iv=iv, segment_size=128)
    return c.decrypt(data)

def register(host, account_id_b64, pin):
    ambassador = os.urandom(16)
    bright = bytearray(BRIGHT_BASE)
    bright[0xc] ^= (pin >> 24) & 0xff
    bright[0xd] ^= (pin >> 16) & 0xff
    bright[0xe] ^= (pin >> 8) & 0xff
    bright[0xf] ^= pin & 0xff

    aeropause = bytes(((ambassador[i] ^ AERO_K[i]) + 0xD3 + i) & 0xff for i in range(16))

    inner = ("Client-Type: %s\r\nNp-AccountId: %s\r\n" % (CLIENT_TYPE, account_id_b64)).encode("ascii")
    payload = bytearray(b"A" * (0x1e0 + len(inner)))
    payload[0xc7:0xc7+8] = aeropause[8:16]
    payload[0x191:0x191+8] = aeropause[0:8]

    iv = make_iv(ambassador, 0)
    enc_inner = aes_cfb(bytes(bright), iv, bytes(inner))
    payload[0x1e0:] = enc_inner

    s = socket.create_connection((host, 9295), timeout=6)
    local_ip = s.getsockname()[0]
    head = (
        "POST /sie/ps5/rp/sess/rgst HTTP/1.1\r\n HTTP/1.1\r\n"
        "HOST: %s\r\n"
        "User-Agent: remoteplay Windows\r\n"
        "Connection: close\r\n"
        "Content-Length: %d\r\n"
        "RP-Version: 1.0\r\n\r\n" % (local_ip, len(payload))
    ).encode("ascii")
    s.sendall(head + bytes(payload))

    s.settimeout(6)
    resp = b""
    try:
        while True:
            chunk = s.recv(8192)
            if not chunk:
                break
            resp += chunk
            if len(resp) > 0x4000:
                break
    except socket.timeout:
        pass
    s.close()

    sep = resp.find(b"\r\n\r\n")
    if sep < 0:
        return {"ok": False, "error": "no HTTP response", "raw": resp.hex()}
    head_str = resp[:sep].decode("ascii", "replace")
    body = resp[sep+4:]

    parts = head_str.split(" ")
    code = int(parts[1]) if len(parts) > 1 and parts[1].isdigit() else 0
    if code != 200:
        reason = 0
        for line in head_str.split("\r\n"):
            if line.lower().startswith("rp-application-reason:"):
                reason = int(line.split(":", 1)[1].strip(), 16)
        return {"ok": False, "error": "HTTP %d reason 0x%08x (%s)" % (code, reason, REASONS.get(reason, "refused"))}

    clen = 0
    for line in head_str.split("\r\n"):
        if line.lower().startswith("content-length:"):
            clen = int(line.split(":", 1)[1].strip())
    if clen and len(body) > clen:
        body = body[:clen]
    if not body:
        return {"ok": False, "error": "empty response body"}

    dec = aes_cfb_decrypt(bytes(bright), make_iv(ambassador, 0), body)
    text = dec.decode("ascii", "replace")
    fields = {}
    for line in text.replace("\r\n", "\n").split("\n"):
        if ":" in line:
            k, v = line.split(":", 1)
            fields[k.strip()] = v.strip()

    out = {"ok": True, "fields": {k: v for k, v in fields.items()}}
    return out

if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit("usage: python tools/regist.py PS5_IP PIN ACCOUNT_ID_B64")
    host = sys.argv[1]
    pin = int(sys.argv[2])
    account_id_b64 = sys.argv[3]
    result = register(host, account_id_b64, pin)
    shown = dict(result)
    if shown.get("ok") and shown.get("fields"):
        shown["fields"] = {
            key: ("<redacted:%d chars>" % len(value) if any(secret in key.lower() for secret in ("key", "seed", "credential", "ap-", "npid", "account")) else value)
            for key, value in shown["fields"].items()
        }
    print(json.dumps(shown, indent=1))
    if result.get("ok"):
        output = os.path.join(tempfile.gettempdir(), "vectis-regist-result.json")
        with open(output, "w") as f:
            json.dump(result, f, indent=1)
        print("full sensitive result written outside the repo: " + output)
