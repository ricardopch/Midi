package com.ricardopch.midi;

import android.app.Activity;
import android.content.ClipData;
import android.content.ContentResolver;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.res.ColorStateList;
import android.database.Cursor;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.RippleDrawable;
import android.media.AudioFormat;
import android.media.AudioManager;
import android.media.AudioTrack;
import android.media.MediaPlayer;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.OpenableColumns;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.Toast;

import com.ricardopch.midi.engine.BasicPitch;
import com.ricardopch.midi.engine.MidiWriter;
import com.ricardopch.midi.engine.Mp3Encoder;
import com.ricardopch.midi.engine.Note;
import com.ricardopch.midi.engine.Progress;
import com.ricardopch.midi.engine.Resampler;
import com.ricardopch.midi.engine.Synth;
import com.ricardopch.midi.engine.WeightFile;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.Locale;

public class MainActivity extends Activity {
    private static final int REQ_PICK = 1;
    private static final int REQ_SAVE = 2;
    private static final int REQ_SAVE_MIDI = 3;
    private static final String[] NOTE_LEVELS = {
            "Menos notas (solo lo que se oye claro)", "Normal", "Más notas (capta más detalles)"
    };
    private static final int OUT_RATE = 44100;

    private static final int C_BG = Color.parseColor("#F4F2FA");
    private static final int C_CARD = Color.WHITE;
    private static final int C_PRIMARY = Color.parseColor("#5B47D6");
    private static final int C_PRIMARY_DARK = Color.parseColor("#3F2FB0");
    private static final int C_TEXT = Color.parseColor("#1D1B26");
    private static final int C_MUTED = Color.parseColor("#6B6880");
    private static final int C_ERROR = Color.parseColor("#B3261E");

    private final Handler ui = new Handler(Looper.getMainLooper());

    private TextView fileInfo, status, resultInfo, percent;
    private LinearLayout progressRow;
    private Spinner instrumentSpinner, notesSpinner;
    private Button pickBtn, previewBtn, convertBtn, cancelBtn, playBtn, saveBtn, shareBtn, saveMidiBtn, shareMidiBtn;
    private ProgressBar progressBar;
    private LinearLayout resultCard;

    private File inputFile;          // copia local de la canción
    private String inputName;
    private File outputFile;         // MP3 generado
    private File midiFile;           // MIDI generado
    private static WeightFile model; // pesos de Basic Pitch (se cargan una vez)
    private volatile boolean busy;
    private volatile boolean cancel;
    private Thread worker;
    private MediaPlayer player;
    private AudioTrack previewTrack;

    // ------------------------------------------------------------------ UI

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().setStatusBarColor(C_PRIMARY_DARK);
        buildUi();
        handleIncoming(getIntent());
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        handleIncoming(intent);
    }

    private int dp(float v) { return Math.round(v * getResources().getDisplayMetrics().density); }

    private void buildUi() {
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(C_BG);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(18), dp(18), dp(18), dp(28));
        scroll.addView(root, new ScrollView.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        TextView title = text("♪ Melodía MIDI", 26, C_TEXT, true);
        root.addView(title);
        TextView sub = text("Pasa a notas todo lo que suena en una canción y crea un MP3 tocado con un solo instrumento, "
                + "además del archivo MIDI (.mid).", 15, C_MUTED, false);
        sub.setPadding(0, dp(4), 0, dp(14));
        root.addView(sub);

        // 1. Canción
        LinearLayout c1 = card(root, "1 · Canción");
        pickBtn = button("Elegir archivo de audio", true);
        c1.addView(pickBtn, fullWidth());
        fileInfo = text("Ningún archivo elegido. Vale MP3, M4A, WAV, OGG, FLAC… e incluso vídeos.", 14, C_MUTED, false);
        fileInfo.setPadding(0, dp(10), 0, 0);
        c1.addView(fileInfo);
        pickBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { pickFile(); }
        });

        // 2. Instrumento
        LinearLayout c2 = card(root, "2 · Instrumento");
        instrumentSpinner = new Spinner(this);
        ArrayAdapter<String> ad = new ArrayAdapter<String>(this, android.R.layout.simple_spinner_item, Synth.NAMES);
        ad.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        instrumentSpinner.setAdapter(ad);
        c2.addView(instrumentSpinner, fullWidth());
        previewBtn = button("🔊  Escuchar ejemplo", false);
        LinearLayout.LayoutParams pl = fullWidth();
        pl.topMargin = dp(10);
        c2.addView(previewBtn, pl);
        previewBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { playPreview(); }
        });
        instrumentSpinner.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(AdapterView<?> parent, View view, int position, long id) { stopPreview(); }
            @Override public void onNothingSelected(AdapterView<?> parent) { }
        });

        // 3. Cantidad de notas
        LinearLayout cn = card(root, "3 · Cantidad de notas");
        notesSpinner = new Spinner(this);
        ArrayAdapter<String> an = new ArrayAdapter<String>(this, android.R.layout.simple_spinner_item, NOTE_LEVELS);
        an.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        notesSpinner.setAdapter(an);
        cn.addView(notesSpinner, fullWidth());
        TextView nh = text("Si faltan notas, elige «Más notas». Si sobran notas o suena demasiado lleno, elige «Menos notas».",
                13, C_MUTED, false);
        nh.setPadding(0, dp(8), 0, 0);
        cn.addView(nh);
        SharedPreferences prefs = getPreferences(MODE_PRIVATE);
        instrumentSpinner.setSelection(Math.max(0, Math.min(Synth.NAMES.length - 1, prefs.getInt("instrumento", 0))));
        notesSpinner.setSelection(Math.max(0, Math.min(2, prefs.getInt("notas", 1))));

        // 4. Convertir
        LinearLayout c3 = card(root, "4 · Convertir");
        convertBtn = button("Crear MP3 y MIDI", true);
        convertBtn.setEnabled(false);
        convertBtn.setAlpha(0.5f);
        c3.addView(convertBtn, fullWidth());
        progressRow = new LinearLayout(this);
        progressRow.setOrientation(LinearLayout.HORIZONTAL);
        progressRow.setGravity(Gravity.CENTER_VERTICAL);
        progressRow.setVisibility(View.GONE);
        progressBar = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        progressBar.setMax(1000);
        progressBar.setProgressTintList(ColorStateList.valueOf(C_PRIMARY));
        progressRow.addView(progressBar, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        percent = text("0 %", 15, C_PRIMARY_DARK, true);
        percent.setGravity(Gravity.END);
        percent.setMinWidth(dp(52));
        progressRow.addView(percent, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        LinearLayout.LayoutParams pb = fullWidth();
        pb.topMargin = dp(12);
        c3.addView(progressRow, pb);
        status = text("", 14, C_MUTED, false);
        status.setPadding(0, dp(8), 0, 0);
        c3.addView(status);
        cancelBtn = button("Cancelar", false);
        cancelBtn.setVisibility(View.GONE);
        LinearLayout.LayoutParams cl = fullWidth();
        cl.topMargin = dp(8);
        c3.addView(cancelBtn, cl);
        convertBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { startConversion(); }
        });
        cancelBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                cancel = true;
                status.setText("Cancelando…");
            }
        });

        // Resultado
        resultCard = card(root, "Resultado");
        resultCard.setVisibility(View.GONE);
        resultInfo = text("", 14, C_TEXT, false);
        resultInfo.setPadding(0, 0, 0, dp(10));
        resultCard.addView(resultInfo);
        playBtn = button("▶  Reproducir", true);
        resultCard.addView(playBtn, fullWidth());
        saveBtn = button("Guardar MP3…", false);
        shareBtn = button("Compartir MP3", false);
        addRow(resultCard, saveBtn, shareBtn);
        saveMidiBtn = button("Guardar MIDI…", false);
        shareMidiBtn = button("Compartir MIDI", false);
        addRow(resultCard, saveMidiBtn, shareMidiBtn);
        playBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { togglePlay(); }
        });
        saveBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { saveFile(outputFile, "audio/mpeg", REQ_SAVE); }
        });
        shareBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { shareFile(outputFile, "audio/mpeg", "Compartir MP3"); }
        });
        saveMidiBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { saveFile(midiFile, "audio/midi", REQ_SAVE_MIDI); }
        });
        shareMidiBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { shareFile(midiFile, "audio/midi", "Compartir MIDI"); }
        });

        TextView tip = text("Las notas se sacan con una red neuronal (Basic Pitch) que reconoce voces e instrumentos a la vez. "
                + "El MP3 y el MIDI duran lo mismo que la canción, así que puedes compararlos. "
                + "El archivo .mid se abre en cualquier programa de música (MuseScore, GarageBand, FL Studio…).", 13, C_MUTED, false);
        tip.setPadding(dp(4), dp(16), dp(4), 0);
        root.addView(tip);

        setContentView(scroll);
    }

    private TextView text(String s, float sp, int color, boolean bold) {
        TextView t = new TextView(this);
        t.setText(s);
        t.setTextSize(sp);
        t.setTextColor(color);
        if (bold) t.setTypeface(Typeface.DEFAULT_BOLD);
        t.setLineSpacing(0, 1.1f);
        return t;
    }

    private LinearLayout card(LinearLayout parent, String heading) {
        LinearLayout c = new LinearLayout(this);
        c.setOrientation(LinearLayout.VERTICAL);
        c.setPadding(dp(16), dp(14), dp(16), dp(16));
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(C_CARD);
        bg.setCornerRadius(dp(16));
        c.setBackground(bg);
        c.setElevation(dp(2));
        TextView h = text(heading, 17, C_PRIMARY_DARK, true);
        h.setPadding(0, 0, 0, dp(10));
        c.addView(h);
        LinearLayout.LayoutParams lp = fullWidth();
        lp.bottomMargin = dp(14);
        parent.addView(c, lp);
        return c;
    }

    private Button button(String label, boolean primary) {
        Button b = new Button(this);
        b.setText(label);
        b.setAllCaps(false);
        b.setTextSize(16);
        b.setTextColor(primary ? Color.WHITE : C_PRIMARY_DARK);
        GradientDrawable shape = new GradientDrawable();
        shape.setCornerRadius(dp(24));
        if (primary) shape.setColor(C_PRIMARY);
        else {
            shape.setColor(Color.parseColor("#ECE8FB"));
        }
        GradientDrawable mask = new GradientDrawable();
        mask.setCornerRadius(dp(24));
        mask.setColor(Color.WHITE);
        b.setBackground(new RippleDrawable(ColorStateList.valueOf(Color.parseColor("#33000000")), shape, mask));
        b.setPadding(dp(16), dp(12), dp(16), dp(12));
        b.setMinHeight(dp(48));
        b.setStateListAnimator(null);
        return b;
    }

    private void addRow(LinearLayout parent, Button left, Button right) {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        LinearLayout.LayoutParams half1 = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        half1.rightMargin = dp(5);
        LinearLayout.LayoutParams half2 = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        half2.leftMargin = dp(5);
        row.addView(left, half1);
        row.addView(right, half2);
        LinearLayout.LayoutParams rl = fullWidth();
        rl.topMargin = dp(10);
        parent.addView(row, rl);
    }

    private LinearLayout.LayoutParams fullWidth() {
        return new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private void setEnabled(Button b, boolean on) {
        b.setEnabled(on);
        b.setAlpha(on ? 1f : 0.5f);
    }

    // ------------------------------------------------------------- archivo

    private void pickFile() {
        if (busy) return;
        Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        i.addCategory(Intent.CATEGORY_OPENABLE);
        i.setType("*/*");
        i.putExtra(Intent.EXTRA_MIME_TYPES, new String[]{"audio/*", "video/*", "application/ogg"});
        try {
            startActivityForResult(i, REQ_PICK);
        } catch (Exception e) {
            Intent g = new Intent(Intent.ACTION_GET_CONTENT);
            g.addCategory(Intent.CATEGORY_OPENABLE);
            g.setType("audio/*");
            try {
                startActivityForResult(g, REQ_PICK);
            } catch (Exception e2) {
                toast("No hay ninguna app para elegir archivos.");
            }
        }
    }

    private void handleIncoming(Intent intent) {
        if (intent == null) return;
        Uri uri = null;
        if (Intent.ACTION_SEND.equals(intent.getAction())) {
            Object o = intent.getParcelableExtra(Intent.EXTRA_STREAM);
            if (o instanceof Uri) uri = (Uri) o;
        } else if (Intent.ACTION_VIEW.equals(intent.getAction())) {
            uri = intent.getData();
        }
        if (uri != null && !busy) loadInput(uri);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (resultCode != RESULT_OK || data == null || data.getData() == null) return;
        if (requestCode == REQ_PICK) loadInput(data.getData());
        else if (requestCode == REQ_SAVE) copyFileTo(outputFile, data.getData(), "MP3 guardado ✔");
        else if (requestCode == REQ_SAVE_MIDI) copyFileTo(midiFile, data.getData(), "MIDI guardado ✔");
    }

    private void loadInput(final Uri uri) {
        busy = true;
        stopPlayer();
        setEnabled(pickBtn, false);
        setEnabled(convertBtn, false);
        resultCard.setVisibility(View.GONE);
        fileInfo.setTextColor(C_MUTED);
        fileInfo.setText("Cargando archivo…");
        final String name = displayName(uri);
        new Thread(new Runnable() {
            @Override public void run() {
                String err = null;
                File[] prev = getCacheDir().listFiles();
                if (prev != null) for (File pf : prev) if (pf.getName().startsWith("entrada.")) pf.delete();
                File dst = new File(getCacheDir(), "entrada." + extension(name));
                InputStream in = null;
                OutputStream out = null;
                long size = 0;
                try {
                    in = getContentResolver().openInputStream(uri);
                    if (in == null) throw new IOException("No se puede abrir");
                    out = new FileOutputStream(dst);
                    byte[] buf = new byte[1 << 16];
                    int n;
                    while ((n = in.read(buf)) > 0) {
                        out.write(buf, 0, n);
                        size += n;
                    }
                } catch (Throwable e) {
                    err = "No se pudo leer el archivo: " + e.getMessage();
                } finally {
                    closeQuietly(in);
                    closeQuietly(out);
                }
                final String error = err;
                final long fsize = size;
                final File f = dst;
                ui.post(new Runnable() {
                    @Override public void run() {
                        busy = false;
                        setEnabled(pickBtn, true);
                        if (error != null) {
                            inputFile = null;
                            fileInfo.setTextColor(C_ERROR);
                            fileInfo.setText(error);
                            return;
                        }
                        inputFile = f;
                        inputName = name;
                        fileInfo.setTextColor(C_TEXT);
                        fileInfo.setText("🎵 " + name + "  ·  " + String.format(Locale.US, "%.1f MB", fsize / 1048576.0));
                        setEnabled(convertBtn, true);
                        status.setText("");
                    }
                });
            }
        }).start();
    }

    private String displayName(Uri uri) {
        String name = null;
        Cursor c = null;
        try {
            c = getContentResolver().query(uri, new String[]{OpenableColumns.DISPLAY_NAME}, null, null, null);
            if (c != null && c.moveToFirst()) name = c.getString(0);
        } catch (Exception ignored) {
        } finally {
            if (c != null) c.close();
        }
        if (name == null) name = uri.getLastPathSegment();
        if (name == null) name = "cancion";
        return name;
    }

    // ---------------------------------------------------------- conversión

    private void startConversion() {
        if (busy || inputFile == null) return;
        busy = true;
        cancel = false;
        stopPlayer();
        stopPreview();
        final int instrument = instrumentSpinner.getSelectedItemPosition();
        final int level = notesSpinner.getSelectedItemPosition();
        getPreferences(MODE_PRIVATE).edit().putInt("instrumento", instrument).putInt("notas", level).apply();
        setEnabled(pickBtn, false);
        setEnabled(convertBtn, false);
        instrumentSpinner.setEnabled(false);
        notesSpinner.setEnabled(false);
        resultCard.setVisibility(View.GONE);
        progressRow.setVisibility(View.VISIBLE);
        progressBar.setProgress(0);
        percent.setText("0 %");
        cancelBtn.setVisibility(View.VISIBLE);
        status.setTextColor(C_MUTED);
        status.setText("Leyendo audio…");
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        final File in = inputFile;
        final String base = baseName(inputName);
        worker = new Thread(new Runnable() {
            @Override public void run() { convert(in, base, instrument, level); }
        }, "conversion");
        worker.start();
    }

    private Progress stage(final String label, final float from, final float to) {
        ui.post(new Runnable() {
            @Override public void run() { if (busy) status.setText(label); }
        });
        return new Progress() {
            private long last;

            @Override public void update(float f) {
                long now = System.currentTimeMillis();
                if (now - last < 80 && f < 1f) return;
                last = now;
                final int v = (int) (1000 * (from + (to - from) * Math.max(0f, Math.min(1f, f))));
                ui.post(new Runnable() {
                    @Override public void run() {
                        progressBar.setProgress(v);
                        percent.setText(Math.min(100, v / 10) + " %");
                    }
                });
            }

            @Override public boolean isCancelled() { return cancel; }
        };
    }

    private void convert(File in, String base, int instrument, int level) {
        String error = null;
        File out = null, mid = null;
        int noteCount = 0;
        double seconds = 0;
        boolean truncated = false;
        try {
            AudioDecoder.Result dec = AudioDecoder.decode(in, stage("Leyendo audio…", 0f, 0.12f));
            truncated = dec.truncated;
            seconds = (double) dec.length / dec.sampleRate;
            float[] x = Resampler.resample(dec.pcm, dec.length, dec.sampleRate, BasicPitch.SR,
                    stage("Preparando…", 0.12f, 0.16f));
            dec = null;
            float[][] act = transcribe(x, stage("Reconociendo las notas…", 0.16f, 0.74f));
            x = null;
            List<Note> notes = BasicPitch.notes(act[0], act[1], act[0].length / BasicPitch.KEYS,
                    BasicPitch.Settings.forSensitivity(level));
            act = null;
            noteCount = notes.size();
            if (noteCount == 0) throw new IOException("No se ha encontrado ninguna nota en el audio.");
            File dir = ShareProvider.outDir(this);
            File[] old = dir.listFiles();
            if (old != null) for (File f : old) f.delete();
            mid = new File(dir, safe(base) + "_notas.mid");
            writeBytes(mid, MidiWriter.toBytes(notes, Synth.GM_PROGRAM[instrument]));
            float[] y = new Synth(instrument, OUT_RATE).render(notes, seconds, stage("Tocando con " + Synth.NAMES[instrument] + "…", 0.74f, 0.86f));
            out = new File(dir, safe(base) + "_notas_" + safe(Synth.NAMES[instrument]) + ".mp3");
            writeBytes(out, Mp3Encoder.encode(y, y.length, OUT_RATE, 128, stage("Creando MP3…", 0.86f, 1f)));
        } catch (Progress.Cancelled c) {
            error = "Conversión cancelada.";
        } catch (OutOfMemoryError oom) {
            error = "La canción es demasiado larga para la memoria del móvil. Prueba con un archivo más corto.";
        } catch (Throwable t) {
            String m = t.getMessage();
            error = "Error: " + (m != null ? m : t.getClass().getSimpleName());
        }
        final String err = error;
        final File result = out, midiResult = mid;
        final int nNotes = noteCount;
        final double secs = seconds;
        final boolean trunc = truncated;
        ui.post(new Runnable() {
            @Override public void run() { finishConversion(err, result, midiResult, nNotes, secs, trunc); }
        });
    }

    /** Ejecuta Basic Pitch por ventanas en varios hilos. Devuelve {notas, ataques} (tramas x 88). */
    private float[][] transcribe(float[] x, final Progress p) throws Exception {
        synchronized (MainActivity.class) {
            if (model == null) model = WeightFile.parse(readAsset("bp.bin"));
        }
        final BasicPitch bp = new BasicPitch(model);
        bp.prepare(x);
        final int frames = bp.frameCount();
        final int windows = bp.windowCount();
        final float[] note = new float[frames * BasicPitch.KEYS];
        final float[] onset = new float[frames * BasicPitch.KEYS];
        final AtomicInteger next = new AtomicInteger(), done = new AtomicInteger();
        final Throwable[] failure = new Throwable[1];
        int threads = Math.max(1, Math.min(4, Runtime.getRuntime().availableProcessors()));
        Thread[] pool = new Thread[threads];
        for (int k = 0; k < threads; k++) {
            pool[k] = new Thread(new Runnable() {
                @Override public void run() {
                    try {
                        BasicPitch.Workspace ws = bp.newWorkspace();
                        int w;
                        while (!p.isCancelled() && failure[0] == null && (w = next.getAndIncrement()) < windows) {
                            bp.runWindow(w, ws, note, onset);
                            done.incrementAndGet();
                        }
                    } catch (Throwable t) {
                        synchronized (failure) { if (failure[0] == null) failure[0] = t; }
                    }
                }
            }, "notas-" + k);
            pool[k].setPriority(Thread.NORM_PRIORITY - 1);
            pool[k].start();
        }
        for (Thread t : pool) {
            while (t.isAlive()) {
                t.join(100);
                p.update((float) done.get() / windows);
            }
        }
        if (p.isCancelled()) throw new Progress.Cancelled();
        if (failure[0] instanceof OutOfMemoryError) throw (OutOfMemoryError) failure[0];
        if (failure[0] != null) throw new IOException(failure[0].toString());
        p.update(1f);
        return new float[][]{note, onset};
    }

    private byte[] readAsset(String name) throws IOException {
        InputStream in = getAssets().open(name);
        try {
            java.io.ByteArrayOutputStream bo = new java.io.ByteArrayOutputStream(1 << 18);
            byte[] buf = new byte[1 << 16];
            int n;
            while ((n = in.read(buf)) > 0) bo.write(buf, 0, n);
            return bo.toByteArray();
        } finally {
            closeQuietly(in);
        }
    }

    private static void writeBytes(File f, byte[] data) throws IOException {
        FileOutputStream fo = new FileOutputStream(f);
        try {
            fo.write(data);
        } finally {
            fo.close();
        }
    }

    private void finishConversion(String err, File result, File midi, int notes, double secs, boolean trunc) {
        busy = false;
        getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        setEnabled(pickBtn, true);
        setEnabled(convertBtn, inputFile != null);
        instrumentSpinner.setEnabled(true);
        notesSpinner.setEnabled(true);
        cancelBtn.setVisibility(View.GONE);
        progressRow.setVisibility(View.GONE);
        if (err != null) {
            status.setTextColor(C_ERROR);
            status.setText(err);
            return;
        }
        outputFile = result;
        midiFile = midi;
        status.setTextColor(C_TEXT);
        status.setText("¡Listo!");
        int m = (int) (secs / 60), s = (int) (secs % 60);
        String info = String.format(Locale.getDefault(), "%s\n%s\n%d notas · %d:%02d · MP3 %.1f MB · MIDI %d KB",
                result.getName(), midi.getName(), notes, m, s, result.length() / 1048576.0, (midi.length() + 1023) / 1024);
        if (trunc) info += "\n(Solo se han procesado los primeros " + AudioDecoder.MAX_SECONDS / 60 + " minutos.)";
        resultInfo.setText(info);
        playBtn.setText("▶  Reproducir");
        resultCard.setVisibility(View.VISIBLE);
    }

    // ---------------------------------------------------------- resultado

    private void togglePlay() {
        if (outputFile == null) return;
        try {
            if (player != null) {
                if (player.isPlaying()) {
                    player.pause();
                    playBtn.setText("▶  Reproducir");
                } else {
                    player.start();
                    playBtn.setText("⏸  Pausa");
                }
                return;
            }
            stopPreview();
            player = new MediaPlayer();
            player.setAudioStreamType(AudioManager.STREAM_MUSIC);
            FileInputStream fis = new FileInputStream(outputFile);
            try {
                player.setDataSource(fis.getFD());
            } finally {
                fis.close();
            }
            player.setOnCompletionListener(new MediaPlayer.OnCompletionListener() {
                @Override public void onCompletion(MediaPlayer mp) {
                    stopPlayer();
                }
            });
            player.prepare();
            player.start();
            playBtn.setText("⏸  Pausa");
        } catch (Exception e) {
            stopPlayer();
            toast("No se pudo reproducir: " + e.getMessage());
        }
    }

    private void stopPlayer() {
        if (player != null) {
            try { player.stop(); } catch (Exception ignored) { }
            player.release();
            player = null;
        }
        if (playBtn != null) playBtn.setText("▶  Reproducir");
    }

    private void saveFile(File f, String mime, int request) {
        if (f == null) return;
        Intent i = new Intent(Intent.ACTION_CREATE_DOCUMENT);
        i.addCategory(Intent.CATEGORY_OPENABLE);
        i.setType(mime);
        i.putExtra(Intent.EXTRA_TITLE, f.getName());
        try {
            startActivityForResult(i, request);
        } catch (Exception e) {
            toast("No hay ninguna app para guardar archivos. Usa «Compartir».");
        }
    }

    private void copyFileTo(final File src, final Uri dest, final String okMessage) {
        if (src == null) return;
        new Thread(new Runnable() {
            @Override public void run() {
                String msg;
                InputStream in = null;
                OutputStream out = null;
                try {
                    ContentResolver cr = getContentResolver();
                    in = new FileInputStream(src);
                    out = cr.openOutputStream(dest);
                    if (out == null) throw new IOException("destino no disponible");
                    byte[] buf = new byte[1 << 16];
                    int n;
                    while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
                    out.flush();
                    msg = okMessage;
                } catch (Exception e) {
                    msg = "No se pudo guardar: " + e.getMessage();
                } finally {
                    closeQuietly(in);
                    closeQuietly(out);
                }
                final String m = msg;
                ui.post(new Runnable() {
                    @Override public void run() { toast(m); }
                });
            }
        }).start();
    }

    private void shareFile(File f, String mime, String title) {
        if (f == null) return;
        Uri uri = ShareProvider.uriFor(this, f);
        Intent i = new Intent(Intent.ACTION_SEND);
        i.setType(mime);
        i.putExtra(Intent.EXTRA_STREAM, uri);
        i.setClipData(ClipData.newRawUri(f.getName(), uri));
        i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        try {
            startActivity(Intent.createChooser(i, title));
        } catch (Exception e) {
            toast("No hay apps para compartir.");
        }
    }

    // ------------------------------------------------------------ ejemplo

    private void playPreview() {
        stopPreview();
        stopPlayer();
        final int inst = instrumentSpinner.getSelectedItemPosition();
        setEnabled(previewBtn, false);
        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    float[] y = new Synth(inst, OUT_RATE).render(Synth.demoPhrase(), 6.0, null);
                    final short[] s = new short[y.length];
                    for (int i = 0; i < y.length; i++) s[i] = (short) (Math.max(-1f, Math.min(1f, y[i])) * 32767f);
                    ui.post(new Runnable() {
                        @Override public void run() {
                            setEnabled(previewBtn, true);
                            try {
                                AudioTrack t = new AudioTrack(AudioManager.STREAM_MUSIC, OUT_RATE, AudioFormat.CHANNEL_OUT_MONO,
                                        AudioFormat.ENCODING_PCM_16BIT, s.length * 2, AudioTrack.MODE_STATIC);
                                int w = t.write(s, 0, s.length);
                                if (w <= 0 || t.getState() != AudioTrack.STATE_INITIALIZED) {
                                    t.release();
                                    toast("No se pudo reproducir el ejemplo.");
                                    return;
                                }
                                t.play();
                                previewTrack = t;
                            } catch (Exception e) {
                                toast("No se pudo reproducir el ejemplo.");
                            }
                        }
                    });
                } catch (final Throwable e) {
                    ui.post(new Runnable() {
                        @Override public void run() { setEnabled(previewBtn, true); }
                    });
                }
            }
        }).start();
    }

    private void stopPreview() {
        if (previewTrack != null) {
            try { previewTrack.stop(); } catch (Exception ignored) { }
            previewTrack.release();
            previewTrack = null;
        }
    }

    // ------------------------------------------------------------ varios

    @Override
    protected void onPause() {
        super.onPause();
        if (player != null && player.isPlaying()) {
            player.pause();
            playBtn.setText("▶  Reproducir");
        }
        stopPreview();
    }

    @Override
    protected void onDestroy() {
        cancel = true;
        stopPlayer();
        stopPreview();
        super.onDestroy();
    }

    private void toast(String s) { Toast.makeText(this, s, Toast.LENGTH_LONG).show(); }

    private static String extension(String name) {
        int dot = name == null ? -1 : name.lastIndexOf('.');
        String e = dot >= 0 ? name.substring(dot + 1).toLowerCase(Locale.US) : "";
        if (!e.matches("[a-z0-9]{1,5}")) e = "bin";
        return e;
    }

    private static String baseName(String name) {
        if (name == null) return "cancion";
        int dot = name.lastIndexOf('.');
        return dot > 0 ? name.substring(0, dot) : name;
    }

    private static String safe(String s) {
        String r = s.replaceAll("[^\\p{L}\\p{N}._-]+", "_");
        if (r.length() > 60) r = r.substring(0, 60);
        return r.isEmpty() ? "cancion" : r;
    }

    private static void closeQuietly(java.io.Closeable c) {
        if (c != null) try { c.close(); } catch (IOException ignored) { }
    }
}
