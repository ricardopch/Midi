# Melodía MIDI

App Android que coge una canción (MP3, M4A, WAV, OGG, FLAC… o un vídeo), saca su **melodía principal** y genera un **MP4** con esa melodía tocada al estilo MIDI con **un solo instrumento** a elegir.

**Descargas:**
- Android: [MelodiaMIDI.apk](MelodiaMIDI.apk) (84 KB, Android 6.0 o superior)
- Windows: [MelodiaMIDI.exe](MelodiaMIDI.exe) (104 KB, Windows 7, 8, 10 u 11; no necesita instalación)

## Uso

1. **Elegir archivo de audio** (o compartir una canción a la app desde otra app).
2. Elegir el **instrumento**. Con *Escuchar ejemplo* se oye cómo suena.
3. **Crear MP4**. Al terminar se puede reproducir, guardar donde quieras o compartir.

Instrumentos: piano, piano eléctrico, órgano, guitarra, flauta, violín, clarinete, trompeta, marimba, caja de música, chiptune 8 bits y sintetizador.

El MP4 (audio AAC) dura lo mismo que la canción original, así que las notas caen en el mismo sitio.

## Windows

Doble clic en `MelodiaMIDI.exe` (no hace falta instalar nada; usa .NET Framework 4.5 o posterior, que ya viene con Windows 10 y 11).

1. **Elegir archivo de audio…** o arrastrar la canción a la ventana (o sobre el icono del .exe).
2. Elegir el **instrumento** (*Escuchar ejemplo* para oírlo).
3. **Crear MP4…**: pregunta dónde guardarlo (propone la carpeta de la canción). Después: *Reproducir*, *Abrir carpeta* o *Abrir MP4*.

La primera vez, Windows SmartScreen puede avisar («Windows protegió su PC») porque el programa no está firmado: *Más información* → *Ejecutar de todas formas*.

Para leer el audio y crear el MP4 (AAC) usa los códecs que ya trae Windows (Media Foundation), por eso el .exe es tan pequeño. En las ediciones «N» de Windows sin el *Media Feature Pack* solo lee WAV y guarda el resultado en WAV.

## Cómo funciona

Todo se procesa en el móvil, sin internet ni permisos.

- **Detección de la melodía** (`engine/MelodyExtractor.java`): espectrograma, función de saliencia por suma de armónicos, seguimiento de la línea principal con Viterbi (continuidad de tono + silencios) y segmentación en notas MIDI (afinación global, re-ataques de notas repetidas).
- **Síntesis** (`engine/Synth.java`): instrumentos propios por síntesis aditiva, FM y Karplus-Strong, sin bancos de sonido.
- **MP4**: `MediaCodec` (AAC-LC 128 kbps) + `MediaMuxer`.

Funciona mejor cuando la voz o el instrumento principal se oye claro. En partes sin melodía (intros, solos de batería) puede seguir al acompañamiento.

## Compilar

- Android, sin Gradle: `./build.sh` (aapt + javac + dx + zipalign + apksigner). Ver requisitos dentro del script.
- Windows, desde Linux: `windows/build.sh` (mono-mcs + windres de mingw-w64). El motor en C# se genera a partir del Java con `tools/java2cs.py`, así que las dos versiones dan el mismo resultado, y `tools/check_mf.py` comprueba el acceso a Media Foundation contra las cabeceras de Windows.

Pruebas del motor en escritorio: `test/gen.py` genera mezclas de prueba con melodía conocida, `test/Run.java` (Java) o `test/RunCs.cs` (C#) las procesa y `test/score.py` las puntúa (F1 de notas ≈ 0,88 de media; voces cantadas 0,93–1,00).
