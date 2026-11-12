#!/bin/bash
# Compila el APK sin Gradle: aapt + javac + dx + zipalign + apksigner.
# Requisitos (Ubuntu): apt install aapt apksigner zipalign dalvik-exchange android-sdk-platform-23 default-jdk
# Además, un android.jar de API 34 (con recursos) en ANDROID_JAR_34 para enlazar el manifiesto.
set -euo pipefail
cd "$(dirname "$0")"
J34=${ANDROID_JAR_34:-/home/claude/sdk/aj/android-34/android.jar}
J23=${ANDROID_JAR_23:-/usr/lib/android-sdk/platforms/android-23/android.jar}
KS=${KEYSTORE:-/home/claude/keys/melodia.jks}
KSPASS=${KEYSTORE_PASS:-melodia2026}
OUT=${1:-MelodiaMIDI.apk}
B=build
rm -rf "$B"; mkdir -p "$B/gen" "$B/obj"
aapt package -f -m -J "$B/gen" -M app/AndroidManifest.xml -S app/res -A app/assets -I "$J34" -F "$B/app.unsigned.apk"
# Se compila contra la API 23 para garantizar compatibilidad con Android 6+
javac -nowarn -source 8 -target 8 -encoding UTF-8 -bootclasspath "$J23" -d "$B/obj" $(find app/src "$B/gen" -name '*.java') 2>&1 | grep -v -E "Picked up|warning|^[0-9]+ warning" || true
[ -f "$B/obj/com/ricardopch/midi/MainActivity.class" ] || { echo "javac falló"; exit 1; }
dalvik-exchange --dex --min-sdk-version=23 --output="$B/classes.dex" "$B/obj"
(cd "$B" && aapt add app.unsigned.apk classes.dex > /dev/null)
zipalign -f -p 4 "$B/app.unsigned.apk" "$B/app.aligned.apk"
apksigner sign --ks "$KS" --ks-pass "pass:$KSPASS" --out "$OUT" "$B/app.aligned.apk"
rm -f "$OUT.idsig"
echo "APK: $OUT ($(du -h "$OUT" | cut -f1))"
