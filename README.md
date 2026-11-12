# Melodía MIDI

Coge una canción (MP3, M4A, WAV, OGG, FLAC… o un vídeo), pasa a notas **todo lo que suena** (voces e instrumentos) y genera:

- un **MP3** con esas notas tocadas al estilo MIDI con **un solo instrumento** a elegir, y
- el **archivo MIDI (.mid)**, para abrirlo en MuseScore, GarageBand, FL Studio, Ableton…

En Windows hay además dos opciones con el MIDI en varias pistas: **Spleeter** (separa voz, batería, bajo y resto) y **MuScriptor** (reconoce qué instrumento toca cada nota).

**Descargas:**
- Android: [MelodiaMIDI.apk](MelodiaMIDI.apk) (200 KB, Android 6.0 o superior)
- Windows: [MelodiaMIDI.exe](MelodiaMIDI.exe) (320 KB, Windows 7, 8, 10 u 11; no necesita instalación)

## Uso (Android)

1. **Elegir archivo de audio** (o compartir una canción a la app desde otra app).
2. Elegir el **instrumento** (*Escuchar ejemplo* para oírlo).
3. Elegir la **cantidad de notas**:
   - *Menos notas*: solo lo que se oye claro.
   - *Normal*: recomendado para empezar.
   - *Más notas*: capta todavía más detalles, aunque puede añadir notas de sobra.
4. **Crear MP3 y MIDI**. Al terminar: *Reproducir*, *Guardar MP3…*, *Compartir MP3*, *Guardar MIDI…* y *Compartir MIDI*.

Instrumentos: piano, piano eléctrico, órgano, guitarra, flauta, violín, clarinete, trompeta, marimba, caja de música, chiptune 8 bits y sintetizador. El .mid lleva el instrumento General MIDI equivalente.

El MP3 (128 kbps) y el MIDI duran lo mismo que la canción original, así que las notas caen en el mismo sitio.

## Windows

Doble clic en `MelodiaMIDI.exe` (no hace falta instalar nada; usa .NET Framework 4.5 o posterior, que ya viene con Windows 10 y 11).

1. **Elegir archivo de audio…** o arrastrar la canción a la ventana (o sobre el icono del .exe).
2. Elegir el **instrumento**, el **reconocimiento** y la **cantidad de notas** (con Basic Pitch y Spleeter):
   - *Basic Pitch (1 pista)*: rápido, todo va dentro del programa.
   - *Spleeter (4 pistas)*: separa la canción en voz, batería, bajo y resto, y cada parte va a su pista del MIDI.
   - *MuScriptor (instrumentos)*: una pista por instrumento reconocido.
3. **Crear MP3 y MIDI…**: pregunta dónde guardar el MP3 (propone la carpeta de la canción) y guarda el `.mid` al lado, con el mismo nombre. Después: *Reproducir*, *Abrir MP3*, *Abrir MIDI* o *Carpeta*.

### Spleeter (voz, batería, bajo y resto)

[Spleeter](https://github.com/deezer/spleeter) (Deezer, licencia MIT) separa la canción en cuatro partes con cuatro redes U-Net. Después, Basic Pitch saca las notas de la voz, el bajo y el resto, y un detector de golpes saca el bombo, la caja y el charles de la batería. El MIDI sale con las pistas *Voz*, *Bajo*, *Resto* (con el instrumento elegido) y *Bateria* (canal de percusión). El MP3 toca con el instrumento elegido las notas de las tres primeras.

La primera vez descarga el modelo oficial de Spleeter (146 MB, de la página de Deezer en GitHub), comprueba que está íntegro y lo guarda en `%LOCALAPPDATA%\MelodiaMIDI\spleeter`. No hace falta Python ni ninguna cuenta. La separación está reescrita en C# (`windows/src/Spleeter.cs`) y da el mismo resultado que el modelo original en TensorFlow.

Limitaciones: no distingue guitarra de piano (van juntos en «resto») y la detección de batería es sencilla (bombo, caja y charles).

### MuScriptor (reconoce los instrumentos)

[MuScriptor](https://github.com/muscriptor/muscriptor) (Kyutai y Mirelo, 2026) es el mejor modelo abierto de transcripción multiinstrumento: distingue guitarras, bajo, piano, batería, cuerdas, voz… y el MIDI sale con una pista por instrumento (la batería en el canal de percusión), con el tempo y los compases detectados. El MP3 se toca con el instrumento elegido usando todas las notas menos la batería.

No va dentro del .exe. La primera vez que se usa, el programa:
1. Pide un **token de Hugging Face** (cuenta gratuita). Antes hay que aceptar la licencia del modelo en <https://huggingface.co/MuScriptor/muscriptor-small> (CC BY-NC 4.0: solo uso no comercial). El token solo sirve para esa primera descarga: en cuanto el modelo está en el PC, el programa lo borra y trabaja sin conectarse a Hugging Face, así que también puedes eliminarlo en la web.
2. Descarga [uv](https://github.com/astral-sh/uv) en esa misma carpeta, y uv instala Python, PyTorch y el paquete `muscriptor` 0.4.0 (alrededor de 1 GB en total, varios minutos).
3. Descarga el modelo *small* (el recomendado para PC sin tarjeta gráfica).

Las siguientes veces va directo. Es bastante más lento que Basic Pitch y necesita Windows de 64 bits.

La primera vez, Windows SmartScreen puede avisar («Windows protegió su PC») porque el programa no está firmado: *Más información* → *Ejecutar de todas formas*.

Para leer el audio usa los códecs que ya trae Windows (Media Foundation); en las ediciones «N» de Windows sin el *Media Feature Pack* solo lee WAV.

## Cómo funciona

Todo se procesa en el dispositivo, sin internet ni permisos.

- **Notas** (`engine/BasicPitch.java`): transcripción polifónica con la red neuronal **Basic Pitch** de Spotify (Apache 2.0, ver `third_party/basic-pitch`), que reconoce varias notas a la vez en cualquier instrumento o voz. La inferencia está reescrita en Java puro (CQT de 9 octavas, apilado de armónicos y 6 convoluciones), reparte la canción en ventanas de 2 s entre los núcleos del procesador y da la misma salida que el modelo original (diferencia < 1e-4). Después, el algoritmo de notas de Basic Pitch (ataques + seguimiento de energía) con tres niveles de sensibilidad.
- **MIDI** (`engine/MidiWriter.java`): SMF tipo 0, 480 pulsos por negra a 120 ppm.
- **Síntesis** (`engine/Synth.java`): instrumentos propios por síntesis aditiva, FM y Karplus-Strong, sin bancos de sonido.
- **MP3** (`engine/Mp3Encoder.java`): codificador propio MPEG-1 Layer III (banco de filtros polifásico, MDCT, cuantización y Huffman con las tablas de la norma), igual en Android y Windows, sin dependencias.

Velocidad orientativa: en un PC, una canción de 4 minutos tarda del orden de medio minuto; en el móvil, algo más según el procesador.

## Compilar

- Android, sin Gradle: `./build.sh` (aapt + javac + dx + zipalign + apksigner). Ver requisitos dentro del script.
- Windows, desde Linux: `windows/build.sh` (mono-mcs + windres de mingw-w64). El motor en C# se genera a partir del Java con `tools/java2cs.py`, así que las dos versiones dan el mismo resultado (mismo .mid, byte a byte), y `tools/check_mf.py` comprueba el acceso a Media Foundation contra las cabeceras de Windows. La integración con MuScriptor está en `windows/src/MuScriptor.cs`.
- Pesos del modelo: `tools/export_basic_pitch.py nmp.onnx app/assets/bp.bin` (el ONNX está en el repositorio de Basic Pitch).

Pruebas del motor en escritorio: `test/RunBP.java` (Java) y `test/RunBP.cs` (C#) procesan un WAV y escriben las notas en CSV, el `.mid` y el resultado sintetizado en WAV.
