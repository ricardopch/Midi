"""Comprueba el interop de Media Foundation (windows/src/MediaFoundation.cs) contra las cabeceras
de Windows de mingw-w64: orden de métodos de cada interfaz COM, IIDs y GUIDs de atributos.
Uso: python3 check_mf.py [CS_FILE] [INCLUDE_DIR]
"""
import re
import sys

cs_path = sys.argv[1] if len(sys.argv) > 1 else "windows/src/MediaFoundation.cs"
inc = sys.argv[2] if len(sys.argv) > 2 else "/usr/share/mingw-w64/include"
cs = open(cs_path, encoding="utf-8").read()
hdrs = {h: open(f"{inc}/{h}", encoding="latin-1").read() for h in ["mfobjects.h", "mfreadwrite.h", "mfapi.h", "mfidl.h", "mftransform.h", "mmreg.h"]}
allh = "\n".join(hdrs.values())
errors = 0


def header_methods(iface):
    m = re.search(r"typedef struct %sVtbl \{(.*?)\} %sVtbl;" % (iface, iface), allh, re.S)
    return re.findall(r"\(STDMETHODCALLTYPE \*(\w+)\)", m.group(1))[3:]


def guid_from_define(name):
    m = re.search(r"(?:DEFINE_GUID|EXTERN_GUID)\s*\(\s*%s\s*,\s*([^)]*)\)" % name, allh)
    if not m:
        return None
    p = [int(x.strip(), 16) for x in m.group(1).split(",")]
    return "%08x-%04x-%04x-%02x%02x-%s" % (p[0], p[1], p[2], p[3], p[4], "".join("%02x" % x for x in p[5:]))


for m in re.finditer(r'\[ComImport, Guid\("([^"]+)"\)[^\]]*\]\s*internal interface (\w+)\s*\{(.*?)\n        \}', cs, re.S):
    iid, name, body = m.group(1).lower(), m.group(2), m.group(3)
    mine = re.findall(r"\b(?:void|int)\s+(\w+)\(", body)
    mine = ["Finalize" if x == "DoFinalize" else x for x in mine]
    ref = header_methods(name)
    ok = mine == ref
    hiid = guid_from_define("IID_" + name)
    print(f"{name:16s} métodos {len(mine):2d}/{len(ref):2d} {'OK' if ok else 'DISTINTO'}   IID {'OK' if hiid == iid else 'DISTINTO ' + str(hiid)}")
    if not ok:
        errors += 1
        for i, (a, b) in enumerate(zip(mine, ref)):
            if a != b:
                print("   primera diferencia en", i, a, b)
                break
    if hiid != iid:
        errors += 1

for name, val in re.findall(r'Guid (\w+) = new Guid\("([^"]+)"\)', cs):
    ref = guid_from_define(name)
    if ref is None:
        m = re.search(r"DEFINE_MEDIATYPE_GUID\s*\(\s*%s\s*,\s*([^)]+)\)" % name, allh)
        if m:
            code = m.group(1).strip()
            if code.startswith("WAVE_FORMAT_"):
                c = re.search(r"#define %s\s+(0x[0-9a-fA-F]+|\d+)" % code, allh)
                code = c.group(1) if c else None
            if code:
                ref = "%08x-0000-0010-8000-00aa00389b71" % int(code, 0)
    if name == "MFMediaType_Audio" and ref is None:
        ref = "73647561-0000-0010-8000-00aa00389b71"
    status = "OK" if ref == val.lower() else f"DISTINTO (cabecera {ref})"
    if ref != val.lower():
        errors += 1
    print(f"  {name:42s} {status}")

print("ERRORES:", errors)
sys.exit(1 if errors else 0)
