using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace coverutil;

public class CoverPreviewForm : Form
{
    private readonly PictureBox _pictureBox;

    public CoverPreviewForm(string? imagePath, string? artist, string? title)
    {
        Text = (!string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(title))
            ? $"{artist} — {title}"
            : "Cover Preview";

        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;

        const int size = 400;
        _pictureBox = new PictureBox
        {
            Size = new Size(size, size),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black,
            Dock = DockStyle.Fill
        };

        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
        {
            try { _pictureBox.Image = Image.FromFile(imagePath); }
            catch { }
        }

        ClientSize = new Size(size, size);
        Controls.Add(_pictureBox);

        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        _pictureBox.Click += (_, _) => Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _pictureBox.Image?.Dispose();
        base.Dispose(disposing);
    }
}
