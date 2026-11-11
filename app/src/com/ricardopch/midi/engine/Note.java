package com.ricardopch.midi.engine;

/** Nota MIDI: tono (0-127), inicio y fin en segundos, intensidad 0..1. */
public final class Note {
    public int pitch;
    public double start;
    public double end;
    public float velocity;

    public Note(int pitch, double start, double end, float velocity) {
        this.pitch = pitch;
        this.start = start;
        this.end = end;
        this.velocity = velocity;
    }

    public double duration() { return end - start; }

    public static double midiToHz(double midi) {
        return 440.0 * Math.pow(2.0, (midi - 69.0) / 12.0);
    }
}
