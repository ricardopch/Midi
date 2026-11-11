"""Genera audios de prueba (mezclas con melodía conocida) y su verdad de referencia.
Uso: python3 gen.py OUTDIR
"""
import os, sys, subprocess, json
import numpy as np
import pretty_midi
import soundfile as sf

SF2 = "/usr/share/sounds/sf2/FluidR3_GM.sf2"
SR = 22050

# (tono MIDI, pulsos); 0 = silencio
MELODIES = {
    "oda": (100, [(64,1),(64,1),(65,1),(67,1),(67,1),(65,1),(64,1),(62,1),(60,1),(60,1),(62,1),(64,1),(64,1.5),(62,.5),(62,2),
                  (64,1),(64,1),(65,1),(67,1),(67,1),(65,1),(64,1),(62,1),(60,1),(60,1),(62,1),(64,1),(62,1.5),(60,.5),(60,2)]),
    "cumple": (110, [(67,.75),(67,.25),(69,1),(67,1),(72,1),(71,2),(67,.75),(67,.25),(69,1),(67,1),(74,1),(72,2),
                     (67,.75),(67,.25),(79,1),(76,1),(72,1),(71,1),(69,2),(77,.75),(77,.25),(76,1),(72,1),(74,1),(72,2)]),
    "frere": (120, [(65,1),(67,1),(69,1),(65,1),(65,1),(67,1),(69,1),(65,1),(69,1),(70,1),(72,2),(69,1),(70,1),(72,2),
                    (72,.5),(74,.5),(72,.5),(70,.5),(69,1),(65,1),(72,.5),(74,.5),(72,.5),(70,.5),(69,1),(65,1),(65,1),(60,1),(65,2),(65,1),(60,1),(65,2)]),
    "green": (100, [(69,1),(72,2),(74,1),(76,1.5),(77,.5),(76,1),(74,2),(71,1),(67,1.5),(69,.5),(71,1),(72,2),(69,1),(69,1.5),(68,.5),(69,1),(71,2),(68,1),(64,2),
                    (69,1),(72,2),(74,1),(76,1.5),(77,.5),(76,1),(74,2),(71,1),(67,1.5),(69,.5),(71,1),(72,1.5),(71,.5),(69,1),(68,1.5),(66,.5),(68,1),(69,3)]),
    "grace": (80, [(60,1),(65,2),(69,.5),(65,.5),(69,2),(67,1),(65,2),(62,1),(60,2),(60,1),(65,2),(69,.5),(65,.5),(69,2),(67,.5),(69,.5),(72,3),(0,1),
                   (69,1),(72,1.5),(69,.5),(72,.5),(69,.5),(65,2),(60,1),(62,1.5),(65,.5),(65,.5),(62,.5),(60,2),(60,1),(65,2),(69,.5),(65,.5),(69,2),(67,1),(65,3)]),
    "elisa": (70, [(76,.25),(75,.25),(76,.25),(75,.25),(76,.25),(71,.25),(74,.25),(72,.25),(69,.75),(0,.25),(60,.25),(64,.25),(69,.25),(71,.75),(0,.25),(64,.25),(68,.25),(71,.25),(72,.75),(0,.25),(64,.25),
                   (76,.25),(75,.25),(76,.25),(75,.25),(76,.25),(71,.25),(74,.25),(72,.25),(69,.75),(0,.25),(60,.25),(64,.25),(69,.25),(71,.75),(0,.25),(64,.25),(72,.25),(71,.25),(69,1.5)]),
}

CHORDS = {0: [0, 4, 7], 5: [5, 9, 12], 7: [7, 11, 14], 9: [9, 12, 16], 2: [2, 5, 9]}  # I IV V vi ii (relativos a la tónica)


def melody_notes(name, transpose=0, legato=0.92):
    bpm, seq = MELODIES[name]
    spb = 60.0 / bpm
    t, notes = 0.6, []
    for p, b in seq:
        d = b * spb
        if p > 0:
            notes.append((t, t + d * legato, p + transpose))
        t += d
    return notes, t, spb


def tonic_of(notes):
    # tónica aproximada: nota final
    return notes[-1][2] % 12


def build_midi(name, mel_prog, acc, transpose=0, mel_vel=105, drums=True, legato=0.92):
    notes, end, spb = melody_notes(name, transpose, legato)
    pm = pretty_midi.PrettyMIDI(initial_tempo=60 / spb)
    mel = pretty_midi.Instrument(program=mel_prog)
    for s, e, p in notes:
        mel.notes.append(pretty_midi.Note(mel_vel, p, s, e))
    pm.instruments.append(mel)
    if acc:
        tonic = tonic_of(notes)
        bar = 4 * spb if name not in ("cumple",) else 3 * spb
        chords = pretty_midi.Instrument(program=acc.get("chord", 0))
        bass = pretty_midi.Instrument(program=33)
        drum = pretty_midi.Instrument(program=0, is_drum=True)
        t = 0.6
        while t < end:
            inbar = [p for s, e, p in notes if s < t + bar and e > t]
            best, bscore = 0, -1
            for root, iv in CHORDS.items():
                pcs = {(tonic + root + i) % 12 for i in iv}
                sc = sum(1 for p in inbar if p % 12 in pcs) + (0.5 if root == 0 else 0)
                if sc > bscore:
                    best, bscore = root, sc
            base = 48 + (tonic + best) % 12
            for k in range(int(round(bar / spb))):
                bt = t + k * spb
                if bt >= end:
                    break
                if acc.get("style") == "arp":
                    seq = [0, CHORDS[best][1], CHORDS[best][2], CHORDS[best][1]]
                    for q in range(2):
                        at = bt + q * spb / 2
                        chords.notes.append(pretty_midi.Note(acc.get("cv", 60), base - 12 + seq[(2 * k + q) % 4] + (12 if q else 0), at, at + spb * 0.9))
                    continue
                if acc.get("style") == "pad":
                    if k == 0:
                        for i in CHORDS[best]:
                            chords.notes.append(pretty_midi.Note(acc.get("cv", 60), base + i, bt, min(end, t + bar)))
                else:
                    for i in CHORDS[best]:
                        chords.notes.append(pretty_midi.Note(acc.get("cv", 60), base + i, bt, bt + spb * 0.8))
                bass.notes.append(pretty_midi.Note(80, base - 12, bt, bt + spb * 0.9))
                if drums:
                    drum.notes.append(pretty_midi.Note(95, 36 if k % 2 == 0 else 38, bt, bt + 0.1))
                    drum.notes.append(pretty_midi.Note(70, 42, bt, bt + 0.05))
                    drum.notes.append(pretty_midi.Note(55, 42, bt + spb / 2, bt + spb / 2 + 0.05))
            t += bar
        pm.instruments += [chords, bass]
        if drums:
            pm.instruments.append(drum)
    return pm, notes, end


def render(pm, path):
    mid = path + ".mid"
    pm.write(mid)
    subprocess.run(["fluidsynth", "-ni", "-g", "0.7", "-r", str(SR), "-F", path, SF2, mid],
                   check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


FORMANTS = {"a": [(730, 1.0), (1090, 0.5), (2440, 0.25)], "e": [(530, 1.0), (1840, 0.4), (2480, 0.25)],
            "i": [(300, 1.0), (2290, 0.3), (3010, 0.2)], "o": [(570, 1.0), (840, 0.6), (2410, 0.15)],
            "u": [(320, 1.0), (870, 0.3), (2240, 0.1)]}


def singer(notes, total, sr=SR, seed=1):
    """Voz sintética: fuente glotal + formantes, portamento, vibrato y consonantes."""
    rng = np.random.default_rng(seed)
    n = int(total * sr) + sr
    f0 = np.zeros(n)
    amp = np.zeros(n)
    vow = np.zeros(n, dtype=int)
    vowels = list(FORMANTS)
    for i, (s, e, p) in enumerate(notes):
        a, b = int(s * sr), int(e * sr)
        f0[a:b] = 440 * 2 ** ((p - 69) / 12)
        env = np.ones(b - a)
        ra = min(len(env), int(0.04 * sr))
        env[:ra] = np.linspace(0, 1, ra)
        rb = min(len(env), int(0.05 * sr))
        env[-rb:] *= np.linspace(1, 0.2, rb)
        amp[a:b] = env * (0.8 + 0.2 * rng.random())
        vow[a:b] = rng.integers(len(vowels))
    # portamento: suavizar f0 en cents (solo donde hay voz)
    cents = np.where(f0 > 0, 1200 * np.log2(np.maximum(f0, 1) / 440), np.nan)
    filled = cents.copy()
    idx = np.where(~np.isnan(filled))[0]
    filled = np.interp(np.arange(n), idx, filled[idx])
    k = int(0.04 * sr)
    ker = np.ones(k) / k
    smooth = np.convolve(filled, ker, mode="same")
    t = np.arange(n) / sr
    # vibrato que empieza ~150 ms después de cada ataque
    onset_age = np.zeros(n)
    last = 0
    for s, e, p in notes:
        a, b = int(s * sr), int(e * sr)
        onset_age[a:b] = t[a:b] - s
    vib = 35 * np.clip((onset_age - 0.15) / 0.2, 0, 1) * np.sin(2 * np.pi * 5.5 * t)
    jitter = np.convolve(rng.normal(0, 4, n), np.ones(400) / 400, mode="same") * 5
    fr = 440 * 2 ** ((smooth + vib + jitter) / 1200)
    phase = 2 * np.pi * np.cumsum(fr) / sr
    y = np.zeros(n)
    block = 512
    for kh in range(1, 40):
        fk = kh * fr
        w = np.zeros(n)
        for vi, v in enumerate(vowels):
            m = vow == vi
            if not m.any():
                continue
            g = np.zeros(n)
            for F, A in FORMANTS[v]:
                bw = 80 + F * 0.06
                g += A / (1 + ((fk - F) / bw) ** 2)
            w[m] = g[m]
        src = kh ** -1.0
        part = src * (0.15 + w) * np.sin(kh * phase)
        part[fk > sr * 0.45] = 0
        y += part
    y *= amp
    # consonantes: ráfaga de ruido al inicio de algunas notas
    for i, (s, e, p) in enumerate(notes):
        if i % 2 == 0:
            a = int(s * sr)
            L = int(0.03 * sr)
            nz = rng.normal(0, 1, L) * np.linspace(1, 0, L)
            nz = np.diff(np.concatenate([[0], nz]))
            y[a:a + L] += 0.4 * nz
    y /= np.max(np.abs(y)) + 1e-9
    return y


def main(out):
    os.makedirs(out, exist_ok=True)
    cases = [
        ("flauta_solo", "oda", 73, None, 0, False),
        ("voz_oohs_pop", "cumple", 53, {"chord": 0, "cv": 62}, 0, True),
        ("guitarra_strings", "green", 27, {"chord": 48, "style": "pad", "cv": 55}, 0, True),
        ("piano_piano", "elisa", 0, {"chord": 0, "cv": 45}, 0, False),
        ("violin_pad", "grace", 40, {"chord": 48, "style": "pad", "cv": 60}, 0, False),
        ("sax_pop", "frere", 65, {"chord": 25, "cv": 60}, -12, True),
        ("trompeta_pop", "oda", 56, {"chord": 0, "cv": 65}, 0, True),
        ("lead_pop", "green", 81, {"chord": 4, "cv": 60}, 0, True),
        ("piano_arpegios", "green", 0, {"chord": 0, "style": "arp", "cv": 70}, 0, False),
        ("piano_arp_oda", "oda", 0, {"chord": 0, "style": "arp", "cv": 75}, 0, False),
    ]
    meta = {}
    for name, mel, prog, acc, tr, drums in cases:
        pm, notes, end = build_midi(mel, prog, acc, tr, drums=drums)
        path = os.path.join(out, name + ".wav")
        render(pm, path)
        meta[name] = notes
        print("ok", name)
    # voces sintéticas mezcladas con acompañamiento
    for name, mel, tr, acc, drums, gain in [("cantante_solo", "grace", 0, None, False, 0),
                                           ("cantante_pop", "cumple", -5, {"chord": 0, "cv": 60}, True, 0.6),
                                           ("cantante_hombre_pop", "oda", -12, {"chord": 25, "cv": 60}, True, 0.6),
                                           ("cantante_grave_pop", "grace", -17, {"chord": 0, "cv": 60}, True, 0.6),
                                           ("cantante_mujer_pop", "frere", 7, {"chord": 0, "cv": 65}, True, 0.6),
                                           ("cantante_rock", "cumple", -12, {"chord": 30, "cv": 70}, True, 0.8)]:
        notes, end, spb = melody_notes(mel, tr, legato=0.97)
        v = singer(notes, end + 1)
        if acc:
            pm, _, _ = build_midi(mel, 0, acc, tr, drums=drums)
            pm.instruments = pm.instruments[1:]  # sin la melodía MIDI
            tmp = os.path.join(out, name + "_acc.wav")
            render(pm, tmp)
            a, _ = sf.read(tmp)
            if a.ndim > 1:
                a = a.mean(axis=1)
            L = max(len(a), len(v))
            mix = np.zeros(L)
            mix[:len(v)] += v
            a = a / (np.max(np.abs(a)) + 1e-9)
            mix[:len(a)] += gain * a
            v = mix / np.max(np.abs(mix)) * 0.9
            os.remove(tmp)
            os.remove(tmp + ".mid")
        sf.write(os.path.join(out, name + ".wav"), v, SR, subtype="PCM_16")
        meta[name] = notes
        print("ok", name)
    json.dump(meta, open(os.path.join(out, "truth.json"), "w"))


if __name__ == "__main__":
    main(sys.argv[1])
