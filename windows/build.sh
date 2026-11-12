#!/bin/bash
# Compila MelodiaMIDI.exe (Windows, .NET Framework 4.x) desde Linux.
# Requisitos (Ubuntu): apt install mono-mcs mono-devel binutils-mingw-w64-x86-64 python3 python3-pil
set -euo pipefail
cd "$(dirname "$0")"
OUT=${1:-../MelodiaMIDI.exe}
B=build
mkdir -p "$B"
# 1) motor: se genera desde el código Java para que Android y Windows sean idénticos
python3 ../tools/java2cs.py ../app/src/com/ricardopch/midi/engine engine
# 2) comprobar el interop de Media Foundation contra las cabeceras de Windows
python3 ../tools/check_mf.py src/MediaFoundation.cs > "$B/check_mf.txt" || { cat "$B/check_mf.txt"; exit 1; }
# 3) recursos: icono, manifiesto (alta resolución) e información de versión
[ -f res/app.ico ] || python3 ../tools/make_icon.py res/app.ico
(cd res && x86_64-w64-mingw32-windres --codepage=65001 -O res -i app.rc -o "../$B/app.res")
# 4) compilar
mcs -codepage:utf8 -optimize+ -target:winexe -platform:anycpu -sdk:4.5 \
    -r:System.Windows.Forms.dll -r:System.Drawing.dll -win32res:"$B/app.res" \
    -out:"$OUT" engine/*.cs src/*.cs
echo "EXE: $OUT ($(du -h "$OUT" | cut -f1))"
