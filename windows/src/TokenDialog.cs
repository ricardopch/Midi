using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MelodiaMidi
{
    /** Pide (una sola vez) el token de Hugging Face que MuScriptor necesita para descargar su modelo. */
    internal sealed class TokenDialog : Form
    {
        private readonly TextBox box;

        public string Token { get { return box.Text.Trim(); } }

        public TokenDialog(Font baseFont, float scale)
        {
            Func<float, int> S = delegate (float v) { return (int)Math.Round(v * scale); };
            Text = "MuScriptor: acceso al modelo";
            Font = baseFont;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(500), S(352));
            BackColor = Color.White;

            Label intro = new Label
            {
                Text = "MuScriptor es gratuito para uso no comercial, pero sus autores piden aceptar su licencia (CC BY-NC 4.0) "
                     + "con una cuenta de Hugging Face, que también es gratuita. Solo hay que hacerlo una vez:",
                Location = new Point(S(16), S(14)),
                Size = new Size(S(468), S(56))
            };
            Label s1 = new Label { Text = "1. Inicia sesión y pulsa «Agree and access repository» en la página del modelo:", Location = new Point(S(16), S(74)), Size = new Size(S(468), S(36)) };
            LinkLabel l1 = new LinkLabel { Text = MuScriptor.ModelPage, Location = new Point(S(32), S(110)), Size = new Size(S(452), S(20)) };
            l1.LinkClicked += delegate { Open(MuScriptor.ModelPage); };
            Label s2 = new Label { Text = "2. Crea un token de tipo «Read» y cópialo:", Location = new Point(S(16), S(138)), Size = new Size(S(468), S(20)) };
            LinkLabel l2 = new LinkLabel { Text = MuScriptor.TokenPage, Location = new Point(S(32), S(160)), Size = new Size(S(452), S(20)) };
            l2.LinkClicked += delegate { Open(MuScriptor.TokenPage); };
            Label s3 = new Label { Text = "3. Pégalo aquí (empieza por «hf_»):", Location = new Point(S(16), S(190)), Size = new Size(S(468), S(20)) };
            box = new TextBox { Location = new Point(S(32), S(214)), Size = new Size(S(452), S(26)), UseSystemPasswordChar = true };
            Label note = new Label
            {
                Text = "Solo se usa para la primera descarga del modelo; después el programa lo borra y puedes eliminarlo también en Hugging Face. La primera conversión instala Python, PyTorch y el modelo (≈ 1 GB) y tarda varios minutos.",
                ForeColor = Color.FromArgb(0x6B, 0x68, 0x80),
                Location = new Point(S(16), S(250)),
                Size = new Size(S(468), S(54))
            };
            Button ok = new Button { Text = "Aceptar", DialogResult = DialogResult.OK, Location = new Point(S(296), S(310)), Size = new Size(S(90), S(30)) };
            Button cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(S(394), S(310)), Size = new Size(S(90), S(30)) };
            ok.Click += delegate (object s, EventArgs e)
            {
                if (Token.Length < 8)
                {
                    MessageBox.Show(this, "Pega el token de Hugging Face (empieza por «hf_»).", "MuScriptor");
                    DialogResult = DialogResult.None;
                }
            };
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { intro, s1, l1, s2, l2, s3, box, note, ok, cancel });
            ActiveControl = box;
            Shown += delegate { box.Focus(); };
        }

        private void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception e) { MessageBox.Show(this, "No se pudo abrir el navegador: " + e.Message + "\n\n" + url, "MuScriptor"); }
        }
    }
}
