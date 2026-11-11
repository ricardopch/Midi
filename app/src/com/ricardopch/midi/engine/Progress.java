package com.ricardopch.midi.engine;

/** Informe de progreso y cancelación para los procesos largos. */
public interface Progress {
    /** fraction: 0..1 dentro de la etapa actual. */
    void update(float fraction);

    boolean isCancelled();

    /** Excepción lanzada cuando el usuario cancela. */
    final class Cancelled extends RuntimeException {
        public Cancelled() { super("Cancelado"); }
    }

    Progress NONE = new Progress() {
        @Override public void update(float fraction) { }
        @Override public boolean isCancelled() { return false; }
    };
}
