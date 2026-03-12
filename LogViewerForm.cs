using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace coverutil;

public class LogViewerForm : Form
{
    private readonly RichTextBox _textBox;

    public LogViewerForm()
    {
        Text = "coverutil — Log";
        Width = 600;
        Height = 420;
        MinimumSize = new System.Drawing.Size(400, 300);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;

        _textBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new System.Drawing.Font("Consolas", 9f),
            BackColor = System.Drawing.Color.FromArgb(18, 18, 18),
            ForeColor = System.Drawing.Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = false
        };

        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            Padding = new Padding(6, 4, 6, 4)
        };

        var refreshBtn = new Button { Text = "Refresh", Width = 80, Left = 0, Top = 4, Height = 26 };
        refreshBtn.Click += (_, _) => LoadLog();

        var fullLogBtn = new Button { Text = "Open full log", Width = 110, Left = 86, Top = 4, Height = 26 };
        fullLogBtn.Click += (_, _) => OpenFullLog();

        var closeBtn = new Button { Text = "Close", Width = 80, Anchor = AnchorStyles.Right | AnchorStyles.Top, Top = 4, Height = 26 };
        closeBtn.Left = buttonPanel.Width - 86;
        closeBtn.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        closeBtn.Click += (_, _) => Close();

        buttonPanel.Controls.AddRange(new Control[] { refreshBtn, fullLogBtn, closeBtn });

        Controls.Add(_textBox);
        Controls.Add(buttonPanel);

        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        LoadLog();
    }

    private void LoadLog()
    {
        if (!File.Exists(Logger.AppLogPath))
        {
            _textBox.Text = "No log entries yet.";
            return;
        }

        try
        {
            _textBox.Text = File.ReadAllText(Logger.AppLogPath);
            _textBox.SelectionStart = _textBox.TextLength;
            _textBox.ScrollToCaret();
        }
        catch (Exception ex)
        {
            _textBox.Text = $"Could not read log: {ex.Message}";
        }
    }

    private static void OpenFullLog()
    {
        if (!File.Exists(Logger.LogPath))
        {
            MessageBox.Show("No detailed log file found.", "coverutil", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try { Process.Start(new ProcessStartInfo("notepad.exe", Logger.LogPath) { UseShellExecute = true }); }
        catch { }
    }
}
