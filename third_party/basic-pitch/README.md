# Basic Pitch

`app/assets/bp.bin` contiene los pesos del modelo **Basic Pitch** de Spotify
(<https://github.com/spotify/basic-pitch>, modelo `icassp_2022`), Copyright 2022 Spotify AB,
publicados con la licencia Apache 2.0 (ver `LICENSE`).

Cambios: los pesos se han extraído del archivo ONNX original y convertido a un formato propio
con `tools/export_basic_pitch.py`. La inferencia (`BasicPitch.java` / `BasicPitch.cs`) y la
creación de notas son una reimplementación propia en Java/C#, que reproduce la salida del modelo
original (diferencia máxima < 1e-4) y su algoritmo de notas.
