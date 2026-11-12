using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Media;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MelodiaMidi.Engine;

namespace MelodiaMidi
{
    internal sealed class MainForm : Form
    {
        private const int OutRate = 44100;
        private static readonly string[] NoteLevels = { "Menos notas", "Normal", "Más notas" };
        private static readonly string[] Engines = { "Basic Pitch (rápido, incluido)", "MuScriptor (reconoce instrumentos)" };
        private static WeightFile model;
        private static readonly object ModelLock = new object();

        private static readonly Color CBg = Color.FromArgb(0xF4, 0xF2, 0xFA);
        private static readonly Color CPrimary = Color.FromArgb(0x5B, 0x47, 0xD6);
        private static readonly Color CPrimaryDark = Color.FromArgb(0x3F, 0x2F, 0xB0);
        private static readonly Color CSecondary = Color.FromArgb(0xEC, 0xE8, 0xFB);
        private static readonly Color CText = Color.FromArgb(0x1D, 0x1B, 0x26);
        private static readonly Color CMuted = Color.FromArgb(0x6B, 0x68, 0x80);
        private static readonly Color CError = Color.FromArgb(0xB3, 0x26, 0x1E);
        private static readonly Color COk = Color.FromArgb(0x1E, 0x7B, 0x34);

        private readonly float scale;
        private readonly string fontName;

        private Label fileInfo, status, resultInfo;
        private ComboBox instrument, noteLevel, engine;
        private Label engineHint;
        private Button pickBtn, previewBtn, convertBtn, cancelBtn, playBtn, folderBtn, openBtn, openMidiBtn;
        private ProgressBar progress;
        private Panel resultCard;
        private Label tip;
        private int resultTopY;

        private string inputPath;
        private string outputPath;
        private string midiPath;
        private float[] lastOutput;
        private volatile bool busy;
        private volatile bool cancel;
        private SoundPlayer player;
        private bool playing;
        private System.Windows.Forms.Timer playTimer;

        public MainForm(string initialFile)
        {
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            fontName = AudioIO.IsWindows ? "Segoe UI" : "DejaVu Sans";
            Text = "Melodía MIDI";
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font(fontName, 10f);
            BackColor = CBg;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            BuildUi();
            HookDrop(this);
            playTimer = new System.Windows.Forms.Timer();
            playTimer.Tick += delegate { playTimer.Stop(); StopPlayback(); };
            if (!string.IsNullOrEmpty(initialFile) && File.Exists(initialFile)) LoadInput(initialFile);
        }

        private int S(float v) { return (int)Math.Round(v * scale); }

        // ------------------------------------------------------------------ interfaz

        private void BuildUi()
        {
            const int W = 520;           // ancho de las tarjetas (px lógicos)
            const int M = 18;            // margen
            int y = 0;

            Panel header = new Panel { BackColor = CPrimaryDark, Location = new Point(0, 0), Size = new Size(S(W + 2 * M), S(84)) };
            Label title = new Label { Text = "♪ Melodía MIDI", ForeColor = Color.White, Font = new Font(fontName, 18f, FontStyle.Bold), AutoSize = true, Location = new Point(S(M), S(12)), BackColor = Color.Transparent };
            Label sub = new Label { Text = "Pasa a notas todo lo que suena y crea un MP3 y un MIDI.", ForeColor = Color.FromArgb(0xE4, 0xDF, 0xFF), AutoSize = false, Size = new Size(S(W), S(24)), Location = new Point(S(M), S(52)), BackColor = Color.Transparent };
            header.Controls.Add(title);
            header.Controls.Add(sub);
            Controls.Add(header);
            y = 84 + 14;

            // 1. Canción
            Panel c1 = Card("1 · Canción", M, y, W, 120);
            pickBtn = MakeButton("Elegir archivo de audio…", true, 16, 44, W - 32);
            pickBtn.Click += delegate { PickFile(); };
            c1.Controls.Add(pickBtn);
            fileInfo = MakeLabel("Ningún archivo elegido. Vale MP3, M4A, WAV, WMA, FLAC… e incluso vídeos. También puedes arrastrarlo a esta ventana.", 16, 84, W - 32, 34, CMuted, 9f);
            c1.Controls.Add(fileInfo);
            y += 120 + 12;

            // 2. Instrumento, reconocimiento y cantidad de notas
            Panel c2 = Card("2 · Instrumento y notas", M, y, W, 172);
            instrument = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(S(16), S(48)), Size = new Size(S(250), S(30)), Font = new Font(fontName, 11f) };
            instrument.Items.AddRange(Synth.NAMES);
            instrument.SelectedIndex = 0;
            instrument.SelectedIndexChanged += delegate { StopPlayback(); };
            c2.Controls.Add(instrument);
            previewBtn = MakeButton("Escuchar ejemplo", false, 280, 44, W - 296);
            previewBtn.Click += delegate { PlayPreview(); };
            c2.Controls.Add(previewBtn);
            engine = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(S(16), S(92)), Size = new Size(S(250), S(30)), Font = new Font(fontName, 11f) };
            engine.Items.AddRange(Engines);
            engine.SelectedIndex = 0;
            c2.Controls.Add(engine);
            noteLevel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(S(280), S(92)), Size = new Size(S(W - 296), S(30)), Font = new Font(fontName, 11f) };
            noteLevel.Items.AddRange(NoteLevels);
            noteLevel.SelectedIndex = 1;
            c2.Controls.Add(noteLevel);
            engineHint = MakeLabel("", 16, 128, W - 32, 40, CMuted, 8.5f);
            c2.Controls.Add(engineHint);
            engine.SelectedIndexChanged += delegate { UpdateEngineHint(); };
            UpdateEngineHint();
            y += 172 + 12;

            // 3. Crear
            Panel c3 = Card("3 · Crear el MP3 y el MIDI", M, y, W, 150);
            convertBtn = MakeButton("Crear MP3 y MIDI…", true, 16, 44, W - 32);
            convertBtn.Click += delegate { StartConversion(); };
            SetEnabled(convertBtn, false);
            c3.Controls.Add(convertBtn);
            progress = new ProgressBar { Location = new Point(S(16), S(94)), Size = new Size(S(W - 32 - 110), S(18)), Maximum = 1000, Visible = false };
            c3.Controls.Add(progress);
            cancelBtn = MakeButton("Cancelar", false, W - 16 - 100, 88, 100);
            cancelBtn.Height = S(30);
            cancelBtn.Visible = false;
            cancelBtn.Click += delegate { cancel = true; status.Text = "Cancelando…"; };
            c3.Controls.Add(cancelBtn);
            status = MakeLabel("", 16, 120, W - 32, 24, CMuted, 9.5f);
            c3.Controls.Add(status);
            y += 150 + 12;

            // Resultado
            resultTopY = y;
            resultCard = Card("Resultado", M, y, W, 150);
            resultInfo = MakeLabel("", 16, 40, W - 32, 56, CText, 9f);
            resultCard.Controls.Add(resultInfo);
            int bw = (W - 32 - 3 * 8) / 4;
            playBtn = MakeButton("Reproducir", true, 16, 100, bw);
            playBtn.Click += delegate { TogglePlay(); };
            openBtn = MakeButton("Abrir MP3", false, 16 + bw + 8, 100, bw);
            openBtn.Click += delegate { OpenWithDefault(outputPath); };
            openMidiBtn = MakeButton("Abrir MIDI", false, 16 + 2 * (bw + 8), 100, bw);
            openMidiBtn.Click += delegate { OpenWithDefault(midiPath); };
            folderBtn = MakeButton("Carpeta", false, 16 + 3 * (bw + 8), 100, bw);
            folderBtn.Click += delegate { OpenFolder(); };
            resultCard.Controls.Add(playBtn);
            resultCard.Controls.Add(openBtn);
            resultCard.Controls.Add(openMidiBtn);
            resultCard.Controls.Add(folderBtn);
            resultCard.Visible = false;

            tip = MakeLabel("El MP3 suena con el instrumento elegido; el .mid se guarda al lado y se abre en cualquier programa de música. "
                + "Ambos duran lo mismo que la canción.", M + 2, y, W - 4, 44, CMuted, 8.5f);
            Controls.Add(tip);
            ShowResult(false);
        }

        private void UpdateEngineHint()
        {
            bool mus = engine.SelectedIndex == 1;
            noteLevel.Enabled = !mus && !busy;
            engineHint.Text = mus
                ? "MuScriptor pone cada instrumento en su pista del MIDI. Más lento; la 1.ª vez instala ≈ 1 GB y pide una cuenta gratuita de Hugging Face."
                : "Basic Pitch saca todas las notas a una sola pista. ¿Faltan notas? Elige «Más notas». ¿Sobran? «Menos notas».";
        }

        /** Muestra u oculta la tarjeta de resultado y ajusta la altura de la ventana. */
        private void ShowResult(bool show)
        {
            const int W = 520, M = 18;
            resultCard.Visible = show;
            int y = resultTopY + (show ? 150 + 12 : 0);
            tip.Location = new Point(S(M + 2) + AutoScrollPosition.X, S(y) + AutoScrollPosition.Y);
            int want = S(y + 44 + 10);
            // en pantallas bajas, la ventana se limita a la altura disponible y aparece una barra de desplazamiento
            int max = Screen.FromControl(this).WorkingArea.Height - (Height - ClientSize.Height) - S(8);
            if (want > max)
            {
                AutoScroll = true;
                AutoScrollMinSize = new Size(0, want);
                ClientSize = new Size(S(W + 2 * M) + SystemInformation.VerticalScrollBarWidth, max);
            }
            else
            {
                AutoScroll = false;
                ClientSize = new Size(S(W + 2 * M), want);
            }
        }

        private Panel Card(string heading, int x, int y, int w, int h)
        {
            Panel p = new Panel { BackColor = Color.White, Location = new Point(S(x), S(y)), Size = new Size(S(w), S(h)) };
            p.Paint += delegate (object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Color.FromArgb(0xE2, 0xDE, 0xF0)))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            Label l = new Label { Text = heading, ForeColor = CPrimaryDark, Font = new Font(fontName, 12f, FontStyle.Bold), AutoSize = true, Location = new Point(S(14), S(10)) };
            p.Controls.Add(l);
            Controls.Add(p);
            return p;
        }

        private Button MakeButton(string text, bool primary, int x, int y, int w)
        {
            Button b = new Button
            {
                Text = text,
                Location = new Point(S(x), S(y)),
                Size = new Size(S(w), S(36)),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? CPrimary : CSecondary,
                ForeColor = primary ? Color.White : CPrimaryDark,
                Font = new Font(fontName, 10.5f, primary ? FontStyle.Bold : FontStyle.Regular),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = primary ? CPrimaryDark : Color.FromArgb(0xDD, 0xD6, 0xF8);
            b.Tag = primary;
            return b;
        }

        private Label MakeLabel(string text, int x, int y, int w, int h, Color color, float size)
        {
            return new Label { Text = text, ForeColor = color, Location = new Point(S(x), S(y)), Size = new Size(S(w), S(h)), Font = new Font(fontName, size), AutoSize = false };
        }

        private static void SetEnabled(Button b, bool on)
        {
            b.Enabled = on;
            bool primary = b.Tag is bool && (bool)b.Tag;
            if (primary) b.BackColor = on ? CPrimary : Color.FromArgb(0xB9, 0xB0, 0xEA);
            else b.ForeColor = on ? CPrimaryDark : Color.FromArgb(0xA0, 0x9C, 0xB5);
        }

        // ------------------------------------------------------------------ archivo

        private void HookDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += OnDragEnter;
            c.DragDrop += OnDragDrop;
            foreach (Control ch in c.Controls) HookDrop(ch);
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (busy) return;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0 && File.Exists(files[0])) LoadInput(files[0]);
        }

        private void PickFile()
        {
            if (busy) return;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Elige una canción";
                d.Filter = "Audio y vídeo|*.mp3;*.m4a;*.aac;*.wav;*.wma;*.flac;*.ogg;*.opus;*.aif;*.aiff;*.mp4;*.m4v;*.mov;*.mkv;*.webm;*.avi;*.wmv;*.3gp;*.amr|Todos los archivos|*.*";
                d.CheckFileExists = true;
                if (d.ShowDialog(this) == DialogResult.OK) LoadInput(d.FileName);
            }
        }

        private void LoadInput(string path)
        {
            StopPlayback();
            inputPath = path;
            long size = 0;
            try { size = new FileInfo(path).Length; } catch (Exception) { }
            fileInfo.ForeColor = CText;
            fileInfo.Text = Path.GetFileName(path) + "   ·   " + (size / 1048576.0).ToString("0.0") + " MB";
            SetEnabled(convertBtn, true);
            status.Text = "";
            ShowResult(false);
        }

        // ------------------------------------------------------------------ conversión

        private void StartConversion()
        {
            if (busy || inputPath == null) return;
            int inst = instrument.SelectedIndex;
            int level = noteLevel.SelectedIndex;
            bool useMus = engine.SelectedIndex == 1;
            string token = null;
            if (useMus)
            {
                token = MuScriptor.Token();
                if (token == null)
                {
                    using (TokenDialog td = new TokenDialog(Font, scale))
                    {
                        if (td.ShowDialog(this) != DialogResult.OK) return;
                        token = td.Token;
                        try { MuScriptor.SaveToken(token); } catch (Exception) { }
                    }
                }
            }
            string dest;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "Guardar el MP3 (el MIDI se guarda al lado)";
                d.Filter = "Audio MP3 (*.mp3)|*.mp3";
                d.DefaultExt = "mp3";
                d.AddExtension = true;
                d.OverwritePrompt = true;
                d.FileName = Safe(Path.GetFileNameWithoutExtension(inputPath)) + "_notas_" + Safe(Synth.NAMES[inst]) + ".mp3";
                try { d.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(inputPath)); } catch (Exception) { }
                if (d.ShowDialog(this) != DialogResult.OK) return;
                dest = d.FileName;
            }
            busy = true;
            cancel = false;
            StopPlayback();
            SetEnabled(pickBtn, false);
            SetEnabled(convertBtn, false);
            instrument.Enabled = false;
            noteLevel.Enabled = false;
            engine.Enabled = false;
            ShowResult(false);
            progress.Value = 0;
            progress.Visible = true;
            cancelBtn.Visible = true;
            status.ForeColor = CMuted;
            status.Text = "Leyendo audio…";
            string src = inputPath;
            Thread t = new Thread(delegate () { Convert(src, dest, inst, level, useMus, token); });
            t.IsBackground = true;
            t.Name = "conversion";
            t.Start();
        }

        private sealed class StageProgress : IProgress
        {
            private readonly MainForm f;
            private readonly float from, to;
            private int lastTick;

            public StageProgress(MainForm f, string label, float from, float to)
            {
                this.f = f;
                this.from = from;
                this.to = to;
                f.Ui(delegate { if (f.busy) f.status.Text = label; });
            }

            public void update(float x)
            {
                int now = Environment.TickCount;
                if (now - lastTick < 80 && x < 1f) return;
                lastTick = now;
                int v = (int)(1000 * (from + (to - from) * Math.Max(0f, Math.Min(1f, x))));
                f.Ui(delegate { f.progress.Value = Math.Max(0, Math.Min(1000, v)); });
            }

            public bool isCancelled() { return f.cancel; }
        }

        private void Ui(MethodInvoker a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); } catch (Exception) { }
        }

        private void Convert(string src, string dest, int inst, int level, bool useMus, string token)
        {
            string error = null, written = null, midi = null, instruments = null;
            int notes = 0;
            double seconds = 0;
            bool truncated = false;
            float[] y = null;
            string work = null;
            try
            {
                AudioIO.Input input = AudioIO.Decode(src, new StageProgress(this, "Leyendo audio…", 0f, 0.1f));
                truncated = input.Truncated;
                seconds = (double)input.Length / input.SampleRate;
                List<Note> list;
                midi = Path.ChangeExtension(dest, ".mid");
                if (useMus)
                {
                    work = Path.Combine(Path.GetTempPath(), "MelodiaMIDI_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(work);
                    string wav = Path.Combine(work, "entrada.wav");
                    float[] mono = new float[input.Length];
                    Array.Copy(input.Samples, mono, input.Length);
                    File.WriteAllBytes(wav, WavFile.Build(mono, input.SampleRate));
                    mono = null;
                    input = null;
                    Ui(delegate { progress.Style = ProgressBarStyle.Marquee; });
                    byte[] mid = MuScriptor.Transcribe(wav, work, token,
                        delegate (string msg) { Ui(delegate { if (busy) status.Text = msg; }); },
                        new StageProgress(this, "Preparando MuScriptor…", 0.1f, 0.85f));
                    Ui(delegate { progress.Style = ProgressBarStyle.Blocks; });
                    File.WriteAllBytes(midi, mid);
                    MidiReader mr = MidiReader.Parse(mid);
                    list = mr.PitchedNotes();
                    notes = mr.Notes.Count;
                    List<string> names = new List<string>();
                    foreach (string n in mr.TrackNames)
                        if (n.Trim().Length > 0 && !names.Contains(n.Trim())) names.Add(n.Trim());
                    instruments = string.Join(", ", names.ToArray());
                    if (list.Count == 0) throw new IOException("MuScriptor no ha encontrado notas con altura (solo percusión o nada). El MIDI se ha guardado igualmente.");
                }
                else
                {
                    float[] x = Resampler.resample(input.Samples, input.Length, input.SampleRate, BasicPitch.SR,
                        new StageProgress(this, "Preparando…", 0.1f, 0.15f));
                    input = null;
                    float[][] act = Transcribe(x, new StageProgress(this, "Reconociendo las notas…", 0.15f, 0.76f));
                    x = null;
                    list = BasicPitch.notes(act[0], act[1], act[0].Length / BasicPitch.KEYS, BasicPitch.Settings.forSensitivity(level));
                    act = null;
                    notes = list.Count;
                    if (notes == 0) throw new IOException("No se ha encontrado ninguna nota en el audio.");
                    File.WriteAllBytes(midi, MidiWriter.toBytes(list, Synth.GM_PROGRAM[inst]));
                }
                y = new Synth(inst, OutRate).render(list, seconds, new StageProgress(this, "Tocando con " + Synth.NAMES[inst] + "…", 0.85f, 0.93f));
                AudioIO.SaveMp3(y, OutRate, dest, new StageProgress(this, "Creando el MP3…", 0.93f, 1f));
                written = dest;
            }
            catch (CancelledException)
            {
                error = "Conversión cancelada.";
            }
            catch (MuScriptor.TokenException e)
            {
                MuScriptor.ForgetToken();
                error = e.Message;
            }
            catch (OutOfMemoryException)
            {
                error = "No hay memoria suficiente. Prueba con una canción más corta.";
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            finally
            {
                if (work != null) try { Directory.Delete(work, true); } catch (Exception) { }
                Ui(delegate { progress.Style = ProgressBarStyle.Blocks; });
            }
            float[] result = y;
            string midiFile = error == null || File.Exists(midi ?? "") ? midi : null;
            string inst2 = instruments;
            Ui(delegate { Finish(error, written, midiFile, inst2, result, notes, seconds, truncated); });
        }

        private static WeightFile Model()
        {
            lock (ModelLock)
            {
                if (model == null)
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("bp.bin"))
                    using (MemoryStream ms = new MemoryStream())
                    {
                        if (s == null) throw new IOException("Falta el modelo de notas (bp.bin) dentro del programa.");
                        s.CopyTo(ms);
                        model = WeightFile.parse(ms.ToArray());
                    }
                }
                return model;
            }
        }

        /** Basic Pitch por ventanas, repartidas entre los núcleos. Devuelve {notas, ataques} (tramas x 88). */
        private static float[][] Transcribe(float[] x, IProgress p)
        {
            BasicPitch bp = new BasicPitch(Model());
            bp.prepare(x);
            int frames = bp.frameCount(), windows = bp.windowCount();
            float[] note = new float[frames * BasicPitch.KEYS], onset = new float[frames * BasicPitch.KEYS];
            int next = -1, done = 0;
            Exception failure = null;
            object gate = new object();
            Thread[] pool = new Thread[Math.Max(1, Math.Min(8, Environment.ProcessorCount))];
            for (int k = 0; k < pool.Length; k++)
            {
                pool[k] = new Thread(delegate ()
                {
                    try
                    {
                        BasicPitch.Workspace ws = bp.newWorkspace();
                        int w;
                        while (!p.isCancelled() && Volatile.Read(ref failure) == null && (w = Interlocked.Increment(ref next)) < windows)
                        {
                            bp.runWindow(w, ws, note, onset);
                            Interlocked.Increment(ref done);
                        }
                    }
                    catch (Exception e)
                    {
                        lock (gate) { if (failure == null) failure = e; }
                    }
                });
                pool[k].IsBackground = true;
                pool[k].Priority = ThreadPriority.BelowNormal;
                pool[k].Start();
            }
            foreach (Thread t in pool)
                while (!t.Join(100)) p.update((float)Volatile.Read(ref done) / windows);
            if (p.isCancelled()) throw new CancelledException();
            if (failure is OutOfMemoryException) throw (OutOfMemoryException)failure;
            if (failure != null) throw new IOException(failure.Message, failure);
            p.update(1f);
            return new float[][] { note, onset };
        }

        private void Finish(string error, string written, string midi, string instruments, float[] result, int notes, double secs, bool truncated)
        {
            busy = false;
            SetEnabled(pickBtn, true);
            SetEnabled(convertBtn, inputPath != null);
            instrument.Enabled = true;
            engine.Enabled = true;
            UpdateEngineHint();
            progress.Visible = false;
            cancelBtn.Visible = false;
            if (error != null)
            {
                status.ForeColor = CError;
                if (error.Length > 80 || error.IndexOf('\n') >= 0)
                {
                    status.Text = "No se ha podido terminar (ver el aviso).";
                    MessageBox.Show(this, error, "Melodía MIDI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else status.Text = error;
                return;
            }
            outputPath = written;
            midiPath = midi;
            lastOutput = result;
            status.ForeColor = COk;
            status.Text = "¡Listo!";
            long size = 0;
            try { size = new FileInfo(written).Length; } catch (Exception) { }
            int m = (int)(secs / 60), s = (int)(secs % 60);
            string info = Path.GetFileName(written) + "  ·  " + (size / 1048576.0).ToString("0.0") + " MB\n"
                + Path.GetFileName(midi) + "  ·  " + notes + " notas  ·  " + m + ":" + s.ToString("00");
            if (!string.IsNullOrEmpty(instruments)) info += "\nInstrumentos: " + instruments;
            if (truncated) info += "\nSolo se han procesado los primeros " + AudioIO.MaxSeconds / 60 + " minutos.";
            resultInfo.Text = info;
            ShowResult(true);
        }

        // ------------------------------------------------------------------ reproducción

        private void PlaySamples(float[] y, Button owner)
        {
            StopPlayback();
            try
            {
                player = new SoundPlayer(new MemoryStream(WavFile.Build(y, OutRate)));
                player.Play();
                playing = true;
                owner.Text = "Parar";
                playTimer.Interval = Math.Max(500, (int)(1000.0 * y.Length / OutRate) + 300);
                playTimer.Tag = owner;
                playTimer.Start();
            }
            catch (Exception e)
            {
                StopPlayback();
                MessageBox.Show(this, "No se pudo reproducir: " + e.Message, "Melodía MIDI");
            }
        }

        private void StopPlayback()
        {
            if (playTimer != null) playTimer.Stop();
            if (player != null)
            {
                try { player.Stop(); } catch (Exception) { }
                player.Dispose();
                player = null;
            }
            playing = false;
            if (previewBtn != null) previewBtn.Text = "Escuchar ejemplo";
            if (playBtn != null) playBtn.Text = "Reproducir";
        }

        private void PlayPreview()
        {
            if (playing && playTimer.Tag == previewBtn) { StopPlayback(); return; }
            float[] y = new Synth(instrument.SelectedIndex, OutRate).render(Synth.demoPhrase(), 6.0, null);
            PlaySamples(y, previewBtn);
        }

        private void TogglePlay()
        {
            if (playing && playTimer.Tag == playBtn) { StopPlayback(); return; }
            if (lastOutput != null) PlaySamples(lastOutput, playBtn);
        }

        private void OpenFolder()
        {
            if (outputPath == null) return;
            try
            {
                if (AudioIO.IsWindows) Process.Start("explorer.exe", "/select,\"" + outputPath + "\"");
                else Process.Start(Path.GetDirectoryName(outputPath));
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "No se pudo abrir la carpeta: " + e.Message, "Melodía MIDI");
            }
        }

        private void OpenWithDefault(string path)
        {
            if (path == null) return;
            StopPlayback();
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "No se pudo abrir el archivo: " + e.Message, "Melodía MIDI");
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            cancel = true;
            StopPlayback();
            base.OnFormClosing(e);
        }

        private static string Safe(string s)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_') b.Append(c);
                else if (b.Length > 0 && b[b.Length - 1] != '_') b.Append('_');
            }
            string r = b.ToString().Trim('_');
            if (r.Length > 60) r = r.Substring(0, 60);
            return r.Length == 0 ? "cancion" : r;
        }
    }
}
