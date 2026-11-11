# Melodía MIDI

App Android que coge una canción (MP3, M4A, WAV, OGG, FLAC… o un vídeo), saca su **melodía principal** y genera un **MP4** con esa melodía tocada al estilo MIDI con **un solo instrumento** a elegir.

**Descargar el APK:** [MelodiaMIDI.apk](MelodiaMIDI.apk) (84 KB, Android 6.0 o superior)

## Uso

1. **Elegir archivo de audio** (o compartir una canción a la app desde otra app).
2. Elegir el **instrumento**. Con *Escuchar ejemplo* se oye cómo suena.
3. **Crear MP4**. Al terminar se puede reproducir, guardar donde quieras o compartir.

Instrumentos: piano, piano eléctrico, órgano, guitarra, flauta, violín, clarinete, trompeta, marimba, caja de música, chiptune 8 bits y sintetizador.

El MP4 (audio AAC) dura lo mismo que la canción original, así que las notas caen en el mismo sitio.

## Cómo funciona

Todo se procesa en el móvil, sin internet ni permisos.

- **Detección de la melodía** (`engine/MelodyExtractor.java`): espectrograma, función de saliencia por suma de armónicos, seguimiento de la línea principal con Viterbi (continuidad de tono + silencios) y segmentación en notas MIDI (afinación global, re-ataques de notas repetidas).
- **Síntesis** (`engine/Synth.java`): instrumentos propios por síntesis aditiva, FM y Karplus-Strong, sin bancos de sonido.
- **MP4**: `MediaCodec` (AAC-LC 128 kbps) + `MediaMuxer`.

Funciona mejor cuando la voz o el instrumento principal se oye claro. En partes sin melodía (intros, solos de batería) puede seguir al acompañamiento.

## Compilar

Sin Gradle: `./build.sh` (aapt + javac + dx + zipalign + apksigner). Ver requisitos dentro del script.

Pruebas del motor en escritorio: `test/gen.py` genera mezclas de prueba con melodía conocida, `test/Run.java` las procesa y `test/score.py` las puntúa (F1 de notas ≈ 0,88 de media; voces cantadas 0,93–1,00).
