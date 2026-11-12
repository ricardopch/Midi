"""Exporta los pesos de Spleeter 4stems (Deezer, licencia MIT) al formato propio de Melodía MIDI.

Uso: python3 -I export_spleeter.py CARPETA_4stems/model SALIDA.bin
(el modelo está en https://github.com/deezer/spleeter/releases/download/v1.4.0/4stems.tar.gz; hace falta tensorflow)

Por cada instrumento (v = voz, d = batería, b = bajo, o = resto):
  c0..c5  convoluciones 5x5 de paso 2 · c6 última convolución 4x4 dilatada
  t0..t5  convoluciones traspuestas 5x5 de paso 2
  n0..n11 BatchNorm ya convertidas a escala (s) y desplazamiento (t)
Los núcleos se guardan en float16 con el orden [salida][alto][ancho][entrada]; el resto en float32.
"""
import struct
import sys

import numpy as np
import tensorflow as tf

INST = [("v", "vocals"), ("d", "drums"), ("b", "bass"), ("o", "other")]


def name(base, idx):
    return base + ("" if idx == 0 else "_%d" % idx)


def main(ckpt, out):
    r = tf.train.load_checkpoint(ckpt)
    T = {}
    for k, (p, _) in enumerate(INST):
        for i in range(7):
            n = name("conv2d", 7 * k + i)
            T["%s/c%d_k" % (p, i)] = np.transpose(r.get_tensor(n + "/kernel"), (3, 0, 1, 2))   # (kh,kw,C,O) -> (O,kh,kw,C)
            T["%s/c%d_b" % (p, i)] = r.get_tensor(n + "/bias")
        for i in range(6):
            n = name("conv2d_transpose", 6 * k + i)
            T["%s/t%d_k" % (p, i)] = np.transpose(r.get_tensor(n + "/kernel"), (2, 0, 1, 3))   # (kh,kw,O,C) -> (O,kh,kw,C)
            T["%s/t%d_b" % (p, i)] = r.get_tensor(n + "/bias")
        for i in range(12):
            n = name("batch_normalization", 12 * k + i)
            g, b = r.get_tensor(n + "/gamma"), r.get_tensor(n + "/beta")
            m, v = r.get_tensor(n + "/moving_mean"), r.get_tensor(n + "/moving_variance")
            s = g / np.sqrt(v + 1e-3)
            T["%s/n%d_s" % (p, i)] = s
            T["%s/n%d_t" % (p, i)] = b - m * s
    kind = b"spleeter4"
    with open(out, "wb") as f:
        f.write(b"MLDY")
        f.write(struct.pack(">ii", 1, len(T)))
        f.write(struct.pack(">H", len(kind)) + kind)
        for nm in sorted(T):
            a = np.asarray(T[nm], np.float32)
            f32 = not nm.endswith("_k")
            nb = nm.encode()
            f.write(struct.pack(">H", len(nb)) + nb)
            f.write(struct.pack(">b", 1 if f32 else 0))
            f.write(struct.pack(">i", a.ndim))
            for d in a.shape:
                f.write(struct.pack(">i", d))
            f.write(a.astype(">f4" if f32 else ">f2").tobytes())
    print("%s: %d tensores, %d valores" % (out, len(T), sum(np.asarray(a).size for a in T.values())))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
