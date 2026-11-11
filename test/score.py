"""Evalúa notas estimadas frente a la verdad. Uso: python3 score.py DATADIR ESTDIR"""
import sys, os, json
import numpy as np
import mir_eval


def load_est(path):
    rows = []
    if os.path.getsize(path) > 0:
        rows = np.loadtxt(path, delimiter=",", ndmin=2)
    if len(rows) == 0:
        return np.zeros((0, 2)), np.zeros(0)
    return rows[:, :2], rows[:, 2]


def hz(m):
    return 440.0 * 2 ** ((np.asarray(m, float) - 69) / 12)


def frames(iv, p, T, hop=0.01):
    f = np.zeros(int(T / hop) + 1)
    for (s, e), q in zip(iv, p):
        f[int(s / hop):int(e / hop)] = q
    return f


def main(data, est):
    truth = json.load(open(os.path.join(data, "truth.json")))
    tot = []
    for name, notes in truth.items():
        ref_iv = np.array([[s, e] for s, e, p in notes])
        ref_p = np.array([p for s, e, p in notes], float)
        est_iv, est_p = load_est(os.path.join(est, name + ".csv"))
        if len(est_p):
            P, R, F, _ = mir_eval.transcription.precision_recall_f1_overlap(
                ref_iv, hz(ref_p), est_iv, hz(est_p), onset_tolerance=0.1, pitch_tolerance=50, offset_ratio=None)
            # sin importar la octava
            _, _, Fc, _ = mir_eval.transcription.precision_recall_f1_overlap(
                ref_iv, hz(60 + ref_p % 12), est_iv, hz(60 + est_p % 12), onset_tolerance=0.1, pitch_tolerance=50, offset_ratio=None)
        else:
            P = R = F = Fc = 0
        T = max(ref_iv[:, 1].max(), est_iv[:, 1].max() if len(est_p) else 0) + 1
        rf, ef = frames(ref_iv, ref_p, T), frames(est_iv, est_p, T)
        v = rf > 0
        rpa = np.mean((ef[v] > 0) & (np.abs(ef[v] - rf[v]) < 0.5))
        d = np.abs(ef[v] - rf[v]) % 12
        rca = np.mean((ef[v] > 0) & ((d < 0.5) | (d > 11.5)))
        vfa = np.mean(ef[~v] > 0) if (~v).any() else 0
        tot.append((F, Fc, rpa, rca))
        print(f"{name:22s} notas ref={len(ref_p):3d} est={len(est_p):3d}  P={P:.2f} R={R:.2f} F={F:.2f} Foct={Fc:.2f}  RPA={rpa:.2f} RCA={rca:.2f} VFA={vfa:.2f}")
    t = np.mean(tot, axis=0)
    print(f"{'MEDIA':22s} F={t[0]:.3f} Foct={t[1]:.3f} RPA={t[2]:.3f} RCA={t[3]:.3f}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
