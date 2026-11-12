# Melodía MIDI

Coge una canción (MP3, M4A, WAV, OGG, FLAC… o un vídeo), pasa a notas **todo lo que suena** (voces e instrumentos) y genera:

- un **MP4** con esas notas tocadas al estilo MIDI con **un solo instrumento** a elegir, y
- el **archivo MIDI (.mid)**, para abrirlo en MuseScore, GarageBand, FL Studio, Ableton…

**Descargas:**
- Android: [MelodiaMIDI.apk](MelodiaMIDI.apk) (190 KB, Android 6.0 o superior)
- Windows: [MelodiaMIDI.exe](MelodiaMIDI.exe) (260 KB, Windows 7, 8, 10 u 11; no necesita instalación)

## Uso (Android)

1. **Elegir archivo de audio** (o compartir una canción a la app desde otra app).
2. Elegir el **instrumento** (*Escuchar ejemplo* para oírlo).
3. Elegir la **cantidad de notas**:
   - *Menos notas*: solo lo que se oye claro.
   - *Normal*: recomendado para empezar.
   - *Más notas*: capta todavía más detalles, aunque puede añadir notas de sobra.
4. **Crear MP4 y MIDI**. Al terminar: *Reproducir*, *Guardar MP4…*, *Compartir MP4*, *Guardar MIDI…* y *Compartir MIDI*.

Instrumentos: piano, piano eléctrico, órgano, guitarra, flauta, violín, clarinete, trompeta, marimba, caja de música, chiptune 8 bits y sintetizador. El .mid lleva el instrumento General MIDI equivalente.

El MP4 (audio AAC) y el MIDI duran lo mismo que la canción original, así que las notas caen en el mismo sitio.

## Windows

Doble clic en `MelodiaMIDI.exe` (no hace falta instalar nada; usa .NET Framework 4.5 o posterior, que ya viene con Windows 10 y 11).

1. **Elegir archivo de audio…** o arrastrar la canción a la ventana (o sobre el icono del .exe).
2. Elegir el **instrumento** y la **cantidad de notas**.
3. **Crear MP4 y MIDI…**: pregunta dónde guardar el MP4 (propone la carpeta de la canción) y guarda el `.mid` al lado, con el mismo nombre. Después: *Reproducir*, *Abrir MP4*, *Abrir MIDI* o *Carpeta*.

La primera vez, Windows SmartScreen puede avisar («Windows protegió su PC») porque el programa no está firmado: *Más información* → *Ejecutar de todas formas*.

Para leer el audio y crear el MP4 (AAC) usa los códecs que ya trae Windows (Media Foundation). En las ediciones «N» de Windows sin el *Media Feature Pack* solo lee WAV y guarda el resultado en WAV (el MIDI se crea igual).

## Cómo funciona

Todo se procesa en el dispositivo, sin internet ni permisos.

- **Notas** (`engine/BasicPitch.java`): transcripción polifónica con la red neuronal **Basic Pitch** de Spotify (Apache 2.0, ver `third_party/basic-pitch`), que reconoce varias notas a la vez en cualquier instrumento o voz. La inferencia está reescrita en Java puro (CQT de 9 octavas, apilado de armónicos y 6 convoluciones), reparte la canción en ventanas de 2 s entre los núcleos del procesador y da la misma salida que el modelo original (diferencia < 1e-4). Después, el algoritmo de notas de Basic Pitch (ataques + seguimiento de energía) con tres niveles de sensibilidad.
- **MIDI** (`engine/MidiWriter.java`): SMF tipo 0, 480 pulsos por negra a 120 ppm.
- **Síntesis** (`engine/Synth.java`): instrumentos propios por síntesis aditiva, FM y Karplus-Strong, sin bancos de sonido.
- **MP4**: `MediaCodec` (AAC-LC 128 kbps) + `MediaMuxer` en Android; Media Foundation en Windows.

Velocidad orientativa: en un PC, una canción de 4 minutos tarda del orden de medio minuto; en el móvil, algo más según el procesador.

## Compilar

- Android, sin Gradle: `./build.sh` (aapt + javac + dx + zipalign + apksigner). Ver requisitos dentro del script.
- Windows, desde Linux: `windows/build.sh` (mono-mcs + windres de mingw-w64). El motor en C# se genera a partir del Java con `tools/java2cs.py`, así que las dos versiones dan el mismo resultado (mismo .mid, byte a byte), y `tools/check_mf.py` comprueba el acceso a Media Foundation contra las cabeceras de Windows.
- Pesos del modelo: `tools/export_basic_pitch.py nmp.onnx app/assets/bp.bin` (el ONNX está en el repositorio de Basic Pitch).

Pruebas del motor en escritorio: `test/RunBP.java` (Java) y `test/RunBP.cs` (C#) procesan un WAV y escriben las notas en CSV, el `.mid` y el resultado sintetizado en WAV.
