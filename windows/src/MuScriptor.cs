using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MelodiaMidi.Engine;

namespace MelodiaMidi
{
    /**
     * MuScriptor (Kyutai/Mirelo, 2026): transcripción que reconoce los instrumentos.
     * No va dentro del .exe: la primera vez se descarga "uv" (gestor de Python de Astral), que a su vez instala
     * Python, PyTorch y el paquete muscriptor; los pesos del modelo los descarga muscriptor desde Hugging Face
     * con el token del usuario (licencia CC BY-NC 4.0, hay que aceptarla en la web del modelo).
     */
    internal static class MuScriptor
    {
        public const string PackageVersion = "0.4.0";
        public const string ModelSize = "small";
        public const string ModelPage = "https://huggingface.co/MuScriptor/muscriptor-small";
        public const string TokenPage = "https://huggingface.co/settings/tokens";

        public sealed class TokenException : Exception
        {
            public TokenException(string m) : base(m) { }
        }

        /** Una pista del resultado: un instrumento reconocido y sus notas (en segundos de la grabación). */
        public sealed class Track
        {
            public string Name;
            public int Program;
            public bool Drums;
            public readonly List<Note> Notes = new List<Note>();
        }

        /** Grupos de instrumentos de MuScriptor: programa General MIDI que los representa y nombre de la pista. */
        private static readonly Dictionary<string, KeyValuePair<int, string>> Groups = BuildGroups();

        private static Dictionary<string, KeyValuePair<int, string>> BuildGroups()
        {
            string[] g = {
                "acoustic_piano", "0", "Piano", "electric_piano", "2", "Piano electrico", "chromatic_percussion", "8", "Percusion afinada",
                "organ", "16", "Organo", "acoustic_guitar", "24", "Guitarra acustica", "clean_electric_guitar", "26", "Guitarra electrica",
                "distorted_electric_guitar", "29", "Guitarra distorsionada", "acoustic_bass", "32", "Bajo acustico",
                "electric_bass", "33", "Bajo electrico", "violin", "40", "Violin", "viola", "41", "Viola", "cello", "42", "Violonchelo",
                "contrabass", "43", "Contrabajo", "orchestral_harp", "46", "Arpa", "timpani", "47", "Timbales",
                "string_ensemble", "48", "Cuerdas", "synth_strings", "50", "Cuerdas sintetizadas", "voice", "52", "Voz",
                "orchestra_hit", "55", "Golpe de orquesta", "trumpet", "56", "Trompeta", "trombone", "57", "Trombon", "tuba", "58", "Tuba",
                "french_horn", "60", "Trompa", "brass_section", "61", "Metales", "soprano_and_alto_sax", "64", "Saxo alto",
                "tenor_sax", "66", "Saxo tenor", "baritone_sax", "67", "Saxo baritono", "oboe", "68", "Oboe", "english_horn", "69", "Corno ingles",
                "bassoon", "70", "Fagot", "clarinet", "71", "Clarinete", "flutes", "72", "Flauta", "synth_lead", "80", "Sintetizador",
                "synth_pad", "88", "Pad sintetizado", "drums", "0", "Bateria" };
            Dictionary<string, KeyValuePair<int, string>> d = new Dictionary<string, KeyValuePair<int, string>>();
            for (int i = 0; i < g.Length; i += 3) d[g[i]] = new KeyValuePair<int, string>(int.Parse(g[i + 1], CultureInfo.InvariantCulture), g[i + 2]);
            return d;
        }

        public static string AppDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MelodiaMIDI"); }
        }

        private static string TokenFile { get { return Path.Combine(AppDir, "hf_token.txt"); } }

        // ------------------------------------------------------------------ token de Hugging Face

        /** Carpeta de modelos de Hugging Face (la misma que usa huggingface_hub). */
        private static string HubCache()
        {
            string c = Environment.GetEnvironmentVariable("HF_HUB_CACHE");
            if (!string.IsNullOrEmpty(c)) return c;
            string home = Environment.GetEnvironmentVariable("HF_HOME");
            if (!string.IsNullOrEmpty(home)) return Path.Combine(home, "hub");
            string user = Environment.GetEnvironmentVariable("USERPROFILE") ?? Environment.GetEnvironmentVariable("HOME") ?? "";
            return Path.Combine(Path.Combine(Path.Combine(user, ".cache"), "huggingface"), "hub");
        }

        /** ¿Está ya descargado el modelo? Entonces no hace falta token ni conexión con Hugging Face. */
        public static bool ModelCached()
        {
            try
            {
                string snaps = Path.Combine(Path.Combine(HubCache(), "models--MuScriptor--muscriptor-" + ModelSize), "snapshots");
                if (!Directory.Exists(snaps)) return false;
                foreach (string d in Directory.GetDirectories(snaps))
                    if (File.Exists(Path.Combine(d, "model.safetensors"))) return true;
            }
            catch (Exception) { }
            return false;
        }

        /**
         * Token guardado, el de la variable HF_TOKEN, o "" si no hace falta (modelo ya descargado o sesión de
         * Hugging Face iniciada en el PC); null si hay que pedirlo.
         */
        public static string Token()
        {
            try
            {
                if (File.Exists(TokenFile))
                {
                    string t = File.ReadAllText(TokenFile).Trim();
                    if (t.Length > 0) return t;
                }
            }
            catch (Exception) { }
            string env = Environment.GetEnvironmentVariable("HF_TOKEN");
            if (!string.IsNullOrEmpty(env)) return env.Trim();
            string home = Environment.GetEnvironmentVariable("USERPROFILE") ?? Environment.GetEnvironmentVariable("HOME") ?? "";
            if (home.Length > 0 && File.Exists(Path.Combine(Path.Combine(Path.Combine(home, ".cache"), "huggingface"), "token"))) return "";
            return ModelCached() ? "" : null;
        }

        public static void SaveToken(string token)
        {
            Directory.CreateDirectory(AppDir);
            File.WriteAllText(TokenFile, token.Trim());
        }

        public static void ForgetToken()
        {
            try { if (File.Exists(TokenFile)) File.Delete(TokenFile); } catch (Exception) { }
        }

        // ------------------------------------------------------------------ uv

        private static string FindUv()
        {
            string over = Environment.GetEnvironmentVariable("MELODIA_UV");     // para pruebas
            if (!string.IsNullOrEmpty(over) && File.Exists(over)) return over;
            string mine = Path.Combine(Path.Combine(AppDir, "uv"), "uv.exe");
            if (File.Exists(mine)) return mine;
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(Path.PathSeparator))
            {
                try
                {
                    string c = Path.Combine(dir.Trim().Trim('"'), "uv.exe");
                    if (File.Exists(c)) return c;
                }
                catch (Exception) { }
            }
            return null;
        }

        private static string InstallUv(Action<string> status, IProgress p)
        {
            if (!AudioIO.IsWindows) throw new IOException("MuScriptor solo se instala automáticamente en Windows.");
            if (!Environment.Is64BitOperatingSystem) throw new IOException("MuScriptor necesita Windows de 64 bits.");
            string arch = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "").ToUpperInvariant();
            string asset = arch == "ARM64" ? "uv-aarch64-pc-windows-msvc.zip" : "uv-x86_64-pc-windows-msvc.zip";
            string url = "https://github.com/astral-sh/uv/releases/latest/download/" + asset;
            string dir = Path.Combine(AppDir, "uv");
            Directory.CreateDirectory(dir);
            string zip = Path.Combine(AppDir, asset);
            status("Descargando el instalador de Python (uv)…");
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch (Exception) { }   // TLS 1.2
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "MelodiaMIDI";
            using (WebResponse resp = req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (FileStream f = File.Create(zip))
            {
                long total = resp.ContentLength, done = 0;
                byte[] buf = new byte[1 << 16];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (p.isCancelled()) throw new CancelledException();
                    f.Write(buf, 0, n);
                    done += n;
                    if (total > 0) p.update((float)done / total);
                }
            }
            using (ZipArchive za = ZipFile.OpenRead(zip))
            {
                foreach (ZipArchiveEntry e in za.Entries)
                {
                    string name = e.Name.ToLowerInvariant();
                    if (name == "uv.exe" || name == "uvx.exe" || name == "uvw.exe")
                        e.ExtractToFile(Path.Combine(dir, e.Name), true);
                }
            }
            try { File.Delete(zip); } catch (Exception) { }
            string exe = Path.Combine(dir, "uv.exe");
            if (!File.Exists(exe)) throw new IOException("No se pudo instalar uv.");
            return exe;
        }

        // ------------------------------------------------------------------ transcripción

        /**
         * Transcribe un WAV y devuelve una pista por instrumento reconocido. status recibe mensajes para la ventana
         * y p el avance (según el punto de la canción por el que va). La primera vez instala todo lo necesario.
         */
        public static List<Track> Transcribe(string wavPath, string workDir, string token, double seconds, Action<string> status, IProgress p)
        {
            string uv = FindUv() ?? InstallUv(status, p);
            p.update(0f);
            ProcessStartInfo si = new ProcessStartInfo
            {
                FileName = uv,
                // las notas salen por la salida estándar (una por línea, en JSON) según se van reconociendo
                Arguments = "tool run --python 3.12 --from muscriptor==" + PackageVersion + " muscriptor transcribe "
                            + Quote(wavPath) + " -f jsonl -o - -m " + ModelSize,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = workDir
            };
            // con el modelo ya descargado se trabaja sin conexión con Hugging Face y sin token
            bool offline = ModelCached();
            if (offline) si.EnvironmentVariables["HF_HUB_OFFLINE"] = "1";
            else if (!string.IsNullOrEmpty(token)) si.EnvironmentVariables["HF_TOKEN"] = token;
            si.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            si.EnvironmentVariables["PYTHONUTF8"] = "1";
            si.EnvironmentVariables["NO_COLOR"] = "1";

            List<string> log = new List<string>();
            object gate = new object();
            DateTime t0 = DateTime.Now;
            string phase = "Preparando MuScriptor…";
            Dictionary<string, Track> tracks = new Dictionary<string, Track>();
            Dictionary<long, double[]> open = new Dictionary<long, double[]>();     // índice -> {inicio, tono}
            Dictionary<long, string> openInst = new Dictionary<long, string>();
            double reached = 0;
            int events = 0;
            DataReceivedEventHandler onLine = delegate (object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                string line = e.Data.Trim();
                if (line.Length == 0) return;
                lock (gate)
                {
                    if (line[0] == '{')
                    {
                        events++;
                        string type = Field(line, "type");
                        if (type == "start")
                        {
                            long idx = (long)Num(line, "index");
                            double st = Num(line, "start_time");
                            open[idx] = new double[] { st, Num(line, "pitch") };
                            openInst[idx] = Field(line, "instrument") ?? "";
                            if (st > reached) reached = st;
                            phase = "Reconociendo instrumentos y notas…";
                        }
                        else if (type == "end")
                        {
                            long idx = (long)Num(line, "start_event_index");
                            double[] o;
                            if (open.TryGetValue(idx, out o))
                            {
                                AddNote(tracks, openInst[idx], o[0], Num(line, "end_time"), (int)o[1]);
                                open.Remove(idx);
                                openInst.Remove(idx);
                            }
                        }
                        return;
                    }
                    log.Add(line);
                    if (log.Count > 60) log.RemoveAt(0);
                    string ph = PhaseFor(line);
                    if (ph != null) phase = ph;
                }
            };
            using (Process proc = new Process { StartInfo = si })
            {
                proc.OutputDataReceived += onLine;
                proc.ErrorDataReceived += onLine;
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                while (!proc.WaitForExit(500))
                {
                    if (p.isCancelled())
                    {
                        KillTree(proc);
                        throw new CancelledException();
                    }
                    int secs = (int)(DateTime.Now - t0).TotalSeconds;
                    string ph;
                    double r;
                    lock (gate) { ph = phase; r = reached; }
                    if (seconds > 0) p.update((float)Math.Min(0.99, r / seconds));
                    status(ph + "  (" + secs / 60 + ":" + (secs % 60).ToString("00") + ")");
                }
                proc.WaitForExit();
                string all;
                lock (gate) all = string.Join("\n", log.ToArray());
                if (proc.ExitCode != 0)
                {
                    if (all.IndexOf("huggingface", StringComparison.OrdinalIgnoreCase) >= 0
                        && (all.IndexOf("license", StringComparison.OrdinalIgnoreCase) >= 0
                            || all.IndexOf("401", StringComparison.Ordinal) >= 0 || all.IndexOf("403", StringComparison.Ordinal) >= 0
                            || all.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0))
                        throw new TokenException("Hugging Face no ha dado acceso al modelo. Comprueba que has aceptado la licencia en "
                            + ModelPage + " con la misma cuenta del token, y vuelve a intentarlo (te pedirá el token otra vez).");
                    throw new IOException("MuScriptor ha fallado:\n" + Tail(log, 6));
                }
            }
            // notas que quedaron abiertas al terminar
            foreach (KeyValuePair<long, double[]> kv in open) AddNote(tracks, openInst[kv.Key], kv.Value[0], kv.Value[0] + 0.25, (int)kv.Value[1]);
            // el token solo sirve para la primera descarga: una vez guardado el modelo, se borra del PC
            if (ModelCached()) ForgetToken();
            p.update(1f);
            List<Track> res = new List<Track>(tracks.Values);
            res.Sort(delegate (Track a, Track b)
            {
                if (a.Drums != b.Drums) return a.Drums ? 1 : -1;
                return a.Program.CompareTo(b.Program);
            });
            return res;
        }

        private static void AddNote(Dictionary<string, Track> tracks, string inst, double start, double end, int pitch)
        {
            Track t;
            if (!tracks.TryGetValue(inst, out t))
            {
                t = new Track();
                KeyValuePair<int, string> g;
                int prog;
                if (Groups.TryGetValue(inst, out g)) { t.Program = g.Key; t.Name = g.Value; }
                else if (inst.StartsWith("program_") && int.TryParse(inst.Substring(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out prog))
                {
                    t.Program = Math.Max(0, Math.Min(127, prog));
                    t.Name = "Programa " + t.Program;
                }
                else { t.Program = 0; t.Name = inst.Length > 0 ? inst : "Otros"; }
                t.Drums = inst == "drums";
                tracks[inst] = t;
            }
            if (t.Drums) end = start + 0.08;
            else if (end <= start) end = start + 0.05;
            t.Notes.Add(new Note(Math.Max(0, Math.Min(127, pitch)), Math.Max(0, start), end, 0.8f));
        }

        private static string Field(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static double Num(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?[0-9][0-9.eE+-]*)");
            double v;
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        /** Traduce las líneas de uv/muscriptor a un estado legible. */
        private static string PhaseFor(string line)
        {
            string l = line.ToLowerInvariant();
            if (l.StartsWith("downloading") || l.StartsWith("downloaded") || l.StartsWith("installed") || l.StartsWith("resolved")
                || l.StartsWith("prepared") || l.StartsWith("installing") || l.Contains("cpython"))
                return "Instalando MuScriptor (solo la primera vez)…";
            if (l.StartsWith("loading model")) return "Cargando el modelo (la 1.ª vez lo descarga)…";
            if (l.StartsWith("transcribing") || l.Contains("[muscriptor] audio")) return "Reconociendo instrumentos y notas…";
            if (l.Contains("saved midi")) return "Guardando el MIDI…";
            return null;
        }

        private static string Tail(List<string> log, int n)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = Math.Max(0, log.Count - n); i < log.Count; i++)
            {
                string s = log[i];
                if (s.Length > 160) s = s.Substring(0, 160) + "…";
                sb.AppendLine(s);
            }
            return sb.ToString().TrimEnd();
        }

        private static void KillTree(Process proc)
        {
            try
            {
                if (AudioIO.IsWindows)
                {
                    using (Process k = Process.Start(new ProcessStartInfo("taskkill", "/PID " + proc.Id + " /T /F")
                    { UseShellExecute = false, CreateNoWindow = true }))
                        k.WaitForExit(5000);
                }
                if (!proc.HasExited) proc.Kill();
            }
            catch (Exception) { }
        }

        private static string Quote(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }
    }
}
