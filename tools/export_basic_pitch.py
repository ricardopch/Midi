"""Exporta los pesos de Basic Pitch (Spotify, Apache 2.0) desde su ONNX al formato propio de Melodía MIDI.

Uso: python3 -I export_basic_pitch.py basic_pitch/saved_models/icassp_2022/nmp.onnx app/assets/bp.bin
(el ONNX viene en el repositorio https://github.com/spotify/basic-pitch)

Formato (big-endian): "MLDY" | versión(int) | n(int) | tipo(UTF) | n x [nombre(UTF) | f32?(byte) | ndim(int) | dims(int...) | datos]
Aquí todos los tensores van en float32. Los núcleos de convolución mantienen el orden de ONNX (salida, entrada, alto, ancho).
"""
import struct
import sys

import numpy as np
import onnx
from onnx import numpy_helper


def load(path):
    g = onnx.load(path).graph
    init = {i.name: numpy_helper.to_array(i) for i in g.initializer}
    W = {}
    for n in g.node:
        if n.op_type != "Conv":
            continue
        k = init[n.input[1]]
        b = init[n.input[2]] if len(n.input) > 2 and n.input[2] in init else None
        if k.shape == (36, 1, 1, 256):
            W["cqt_i" if "cqt_r" in W else "cqt_r"] = k[:, 0, 0, :]   # primera conv de la octava: real, después imaginaria
        elif k.shape == (1, 1, 1, 256):
            W["lowpass"] = k[0, 0, 0, :]
        else:
            name = {(32, 8, 5, 5): "onset1", (8, 8, 3, 39): "cont1", (1, 8, 5, 5): "cont2",
                    (32, 1, 7, 7): "note1", (1, 32, 7, 3): "note2", (1, 33, 3, 3): "onset2"}[k.shape]
            W[name + "_k"], W[name + "_b"] = k, b
    for n in g.node:
        for i in n.input:
            if i not in init:
                continue
            if n.op_type == "Mul" and init[i].shape == (309, 1, 1):
                W["cqt_scale"] = init[i][:, 0, 0]
            if init[i].shape == (1,) and "BatchNorm" in n.output[0]:
                W["bn_mul" if n.op_type == "Mul" else "bn_add"] = init[i]
    need = ["cqt_r", "cqt_i", "lowpass", "cqt_scale", "bn_mul", "bn_add"] + \
           [p + s for p in ("cont1", "cont2", "note1", "note2", "onset1", "onset2") for s in ("_k", "_b")]
    missing = [k for k in need if k not in W]
    if missing:
        raise SystemExit("Faltan tensores: %s" % missing)
    return {k: np.asarray(W[k], np.float32) for k in need}


def main(src, out):
    T = load(src)
    kind = b"bp"
    with open(out, "wb") as f:
        f.write(b"MLDY")
        f.write(struct.pack(">ii", 1, len(T)))
        f.write(struct.pack(">H", len(kind)) + kind)
        for name in sorted(T):
            a = T[name]
            nb = name.encode()
            f.write(struct.pack(">H", len(nb)) + nb)
            f.write(struct.pack(">b", 1))
            f.write(struct.pack(">i", a.ndim))
            for d in a.shape:
                f.write(struct.pack(">i", d))
            f.write(a.astype(">f4").tobytes())
    print("%s: %d tensores, %d valores" % (out, len(T), sum(a.size for a in T.values())))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
