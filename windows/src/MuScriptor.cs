using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
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
         * Transcribe un WAV y devuelve el MIDI (una pista por instrumento). status recibe mensajes para la ventana.
         * La primera vez instala todo lo necesario.
         */
        public static byte[] Transcribe(string wavPath, string workDir, string token, Action<string> status, IProgress p)
        {
            string uv = FindUv() ?? InstallUv(status, p);
            string outMid = Path.Combine(workDir, "muscriptor.mid");
            if (File.Exists(outMid)) File.Delete(outMid);
            ProcessStartInfo si = new ProcessStartInfo
            {
                FileName = uv,
                Arguments = "tool run --python 3.12 --from muscriptor==" + PackageVersion + " muscriptor transcribe "
                            + Quote(wavPath) + " -o " + Quote(outMid) + " -m " + ModelSize,
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
            DataReceivedEventHandler onLine = delegate (object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                string line = e.Data.Trim();
                if (line.Length == 0) return;
                lock (gate)
                {
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
                    lock (gate) ph = phase;
                    status(ph + "  (" + secs / 60 + ":" + (secs % 60).ToString("00") + ")");
                }
                proc.WaitForExit();
                string all;
                lock (gate) all = string.Join("\n", log.ToArray());
                if (proc.ExitCode != 0 || !File.Exists(outMid))
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
            // el token solo sirve para la primera descarga: una vez guardado el modelo, se borra del PC
            if (ModelCached()) ForgetToken();
            return File.ReadAllBytes(outMid);
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
