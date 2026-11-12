package com.ricardopch.midi.engine;

import java.util.List;

/**
 * Batería sintetizada para las notas de percusión General MIDI (canal 10): bombo, caja, palmas, charles,
 * platos, toms, cencerro… Todo por síntesis (osciladores, ruido y filtros), sin muestras grabadas.
 */
public final class DrumKit {
    private static final int KICK = 0, SNARE = 1, CLAP = 2, HAT_CLOSED = 3, HAT_OPEN = 4, CRASH = 5, RIDE = 6,
            TOM = 7, STICK = 8, COWBELL = 9, TAMBOURINE = 10, CLICK = 11;
    /** Frecuencias de un platillo metálico (parciales no armónicos). */
    private static final double[] METAL = {205.3, 304.4, 369.6, 522.7, 540.0, 800.0};

    private final int sr;
    private int seed = 12345;

    private DrumKit(int sampleRate) { sr = sampleRate; }

    /** Golpes de batería en un búfer de len muestras (sin normalizar). */
    public static float[] render(List<Note> notes, int sampleRate, int len) {
        DrumKit k = new DrumKit(sampleRate);
        float[] out = new float[len];
        for (Note n : notes) k.hit(out, (int) Math.round(n.start * sampleRate), n.pitch, Math.max(0.1f, Math.min(1f, n.velocity)));
        return out;
    }

    private float noise() {
        seed = seed * 1103515245 + 12345;
        return ((seed >> 8) & 0xFFFF) / 32768f - 1f;
    }

    private static int kind(int key) {
        if (key == 35 || key == 36) return KICK;
        if (key == 38 || key == 40) return SNARE;
        if (key == 39) return CLAP;
        if (key == 42 || key == 44) return HAT_CLOSED;
        if (key == 46) return HAT_OPEN;
        if (key == 49 || key == 52 || key == 55 || key == 57) return CRASH;
        if (key == 51 || key == 53 || key == 59) return RIDE;
        if (key == 41 || key == 43 || key == 45 || key == 47 || key == 48 || key == 50) return TOM;
        if (key == 37) return STICK;
        if (key == 56) return COWBELL;
        if (key == 54) return TAMBOURINE;
        if (key < 35) return KICK;
        return CLICK;
    }

    private void hit(float[] out, int start, int key, float v) {
        int k = kind(key);
        double dur;
        if (k == KICK) dur = 0.45;
        else if (k == SNARE) dur = 0.3;
        else if (k == CLAP) dur = 0.3;
        else if (k == HAT_CLOSED) dur = 0.1;
        else if (k == HAT_OPEN) dur = 0.5;
        else if (k == CRASH) dur = 1.8;
        else if (k == RIDE) dur = 1.2;
        else if (k == TOM) dur = 0.6;
        else if (k == STICK) dur = 0.06;
        else if (k == COWBELL) dur = 0.35;
        else if (k == TAMBOURINE) dur = 0.25;
        else dur = 0.06;
        int n = (int) (dur * sr);
        double dt = 1.0 / sr;
        double phase = 0, phase2 = 0;
        double tomF = 82;
        if (key == 43) tomF = 98; else if (key == 45) tomF = 110; else if (key == 47) tomF = 131;
        else if (key == 48) tomF = 147; else if (key == 50) tomF = 175;
        // filtro paso alto de dos etapas para el ruido de platos (y uno suave para la caja)
        double hpCut = (k == SNARE || k == CLAP) ? 900 : (k == CRASH ? 4500 : 7000);
        double rc = 1.0 / (2 * Math.PI * hpCut), a = rc / (rc + dt);
        double h1 = 0, h2 = 0, xPrev = 0, h1Prev = 0, lp = 0, lp2 = 0;
        double lpA = 1 - Math.exp(-2 * Math.PI * 3500.0 / sr);     // paso bajo (2 etapas) para el ruido de caja y palmas
        double[] ph = new double[METAL.length];
        for (int i = 0; i < n; i++) {
            int idx = start + i;
            if (idx < 0) continue;
            if (idx >= out.length) break;
            double t = i * dt;
            double x = noise();
            h1 = a * (h1 + x - xPrev);
            xPrev = x;
            h2 = a * (h2 + h1 - h1Prev);
            h1Prev = h1;
            lp += (h1 - lp) * lpA;
            lp2 += (lp - lp2) * lpA;
            double hp = (k == SNARE || k == CLAP) ? 2.2 * lp2 : h2;
            double y;
            if (k == KICK) {
                double f = 45 + 80 * Math.exp(-t * 28);
                phase += 2 * Math.PI * f * dt;
                y = Math.sin(phase) * Math.exp(-t * 7) + 0.25 * x * Math.exp(-t * 350);
            } else if (k == SNARE) {
                phase += 2 * Math.PI * 185 * dt;
                phase2 += 2 * Math.PI * 330 * dt;
                y = (0.45 * Math.sin(phase) + 0.2 * Math.sin(phase2)) * Math.exp(-t * 28) + 0.75 * hp * Math.exp(-t * 14);
            } else if (k == CLAP) {
                double env = 0;
                for (int b = 0; b < 3; b++) {
                    double tb = t - 0.011 * b;
                    if (tb >= 0) env += Math.exp(-tb * 280);
                }
                if (t > 0.03) env += 0.6 * Math.exp(-(t - 0.03) * 16);
                y = 0.7 * hp * env;
            } else if (k == HAT_CLOSED || k == HAT_OPEN || k == CRASH || k == RIDE || k == TAMBOURINE) {
                double metal = 0;
                for (int m = 0; m < METAL.length; m++) {
                    ph[m] += METAL[m] * (k == RIDE ? 1.6 : 2.0) * dt;
                    metal += (ph[m] - Math.floor(ph[m]) < 0.5) ? 1 : -1;
                }
                metal /= METAL.length;
                double decay = k == HAT_CLOSED ? 55 : (k == HAT_OPEN ? 7 : (k == CRASH ? 2.2 : (k == RIDE ? 3.5 : 16)));
                double mix = k == RIDE ? 0.45 * metal + 0.12 * hp : 0.25 * metal + 0.6 * hp;
                double gain = k == RIDE ? 0.4 : (k == CRASH ? 0.6 : 0.6);
                y = gain * mix * Math.exp(-t * decay) * (k == CRASH ? Math.min(1, t * 400) : 1);
            } else if (k == TOM) {
                double f = tomF * (1 + 0.45 * Math.exp(-t * 18));
                phase += 2 * Math.PI * f * dt;
                y = 0.9 * Math.sin(phase) * Math.exp(-t * 8) + 0.1 * x * Math.exp(-t * 120);
            } else if (k == STICK) {
                phase += 2 * Math.PI * 420 * dt;
                y = 0.6 * Math.sin(phase) * Math.exp(-t * 110) + 0.3 * hp * Math.exp(-t * 180);
            } else if (k == COWBELL) {
                phase += 562 * dt;
                phase2 += 845 * dt;
                double sq = ((phase - Math.floor(phase)) < 0.5 ? 1 : -1) + ((phase2 - Math.floor(phase2)) < 0.5 ? 1 : -1);
                y = 0.22 * sq * Math.exp(-t * 11);
            } else {
                y = 0.4 * hp * Math.exp(-t * 120);
            }
            out[idx] += (float) (y * v);
        }
    }
}
