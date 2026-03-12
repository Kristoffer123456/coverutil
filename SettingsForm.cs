using System;
using System.Windows.Forms;

namespace coverutil;

public class SettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly Action _onSaved;

    private TextBox _clientIdBox = null!;
    private TextBox _clientSecretBox = null!;
    private TextBox _nowPlayingBox = null!;
    private TextBox _outputBox = null!;
    private TextBox _defaultCoverBox = null!;
    private CheckBox _closeToTrayBox = null!;
    private Label _statusLabel = null!;

    public SettingsForm(AppConfig config, Action onSaved)
    {
        _config = config;
        _onSaved = onSaved;
        BuildLayout();
        LoadValues();
    }

    private void BuildLayout()
    {
        Text = "coverutil — Settings";
        Width = 480;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(12);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true,
            Padding = new Padding(8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

        int row = 0;

        // Client ID
        layout.Controls.Add(new Label { Text = "Spotify Client ID:", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row);
        _clientIdBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3) };
        layout.Controls.Add(_clientIdBox, 1, row);
        layout.SetColumnSpan(_clientIdBox, 2);
        row++;

        // Client Secret
        layout.Controls.Add(new Label { Text = "Client Secret:", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row);
        _clientSecretBox = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, Margin = new Padding(3) };
        layout.Controls.Add(_clientSecretBox, 1, row);
        layout.SetColumnSpan(_clientSecretBox, 2);
        row++;

        // Now Playing Path
        layout.Controls.Add(new Label { Text = "now_playing.txt:", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row);
        _nowPlayingBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Margin = new Padding(3) };
        layout.Controls.Add(_nowPlayingBox, 1, row);
        var browseNow = new Button { Text = "Browse", Dock = DockStyle.Fill, Margin = new Padding(3) };
        browseNow.Click += BrowseNowPlaying;
        layout.Controls.Add(browseNow, 2, row);
        row++;

        // Output Path
        layout.Controls.Add(new Label { Text = "Output file:", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row);
        _outputBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Margin = new Padding(3) };
        layout.Controls.Add(_outputBox, 1, row);
        var browseOut = new Button { Text = "Browse", Dock = DockStyle.Fill, Margin = new Padding(3) };
        browseOut.Click += BrowseOutput;
        layout.Controls.Add(browseOut, 2, row);
        row++;

        // Default Cover Path
        layout.Controls.Add(new Label { Text = "Default cover:", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row);
        _defaultCoverBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Margin = new Padding(3) };
        layout.Controls.Add(_defaultCoverBox, 1, row);
        var browseDefault = new Button { Text = "Browse", Dock = DockStyle.Fill, Margin = new Padding(3) };
        browseDefault.Click += BrowseDefaultCover;
        layout.Controls.Add(browseDefault, 2, row);
        row++;

        // Close to tray
        _closeToTrayBox = new CheckBox
        {
            Text = "Close window to tray (uncheck to quit on close)",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(3, 6, 3, 3)
        };
        layout.Controls.Add(_closeToTrayBox, 0, row);
        layout.SetColumnSpan(_closeToTrayBox, 3);
        row++;

        // Status label
        _statusLabel = new Label { Text = "", AutoSize = true, ForeColor = System.Drawing.Color.DarkRed, Margin = new Padding(3) };
        layout.Controls.Add(_statusLabel, 0, row);
        layout.SetColumnSpan(_statusLabel, 3);
        row++;

        // Save button
        var saveBtn = new Button { Text = "Save", Width = 80, Anchor = AnchorStyles.Right, Margin = new Padding(3) };
        saveBtn.Click += Save;
        layout.Controls.Add(saveBtn, 2, row);

        Controls.Add(layout);
    }

    private void LoadValues()
    {
        _clientIdBox.Text = _config.SpotifyClientId;
        _clientSecretBox.Text = _config.SpotifyClientSecret;
        _nowPlayingBox.Text = _config.NowPlayingPath;
        _outputBox.Text = _config.OutputPath;
        _defaultCoverBox.Text = _config.DefaultCoverPath;
        _closeToTrayBox.Checked = _config.CloseToTray;
    }

    private void BrowseNowPlaying(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select now_playing.txt",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (!string.IsNullOrEmpty(_nowPlayingBox.Text))
            dlg.InitialDirectory = System.IO.Path.GetDirectoryName(_nowPlayingBox.Text);
        if (dlg.ShowDialog() == DialogResult.OK)
            _nowPlayingBox.Text = dlg.FileName;
    }

    private void BrowseOutput(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Select output image file",
            Filter = "JPEG (*.jpg)|*.jpg|PNG (*.png)|*.png|All files (*.*)|*.*",
            OverwritePrompt = false
        };
        if (!string.IsNullOrEmpty(_outputBox.Text))
        {
            dlg.InitialDirectory = System.IO.Path.GetDirectoryName(_outputBox.Text);
            dlg.FileName = System.IO.Path.GetFileName(_outputBox.Text);
        }
        if (dlg.ShowDialog() == DialogResult.OK)
            _outputBox.Text = dlg.FileName;
    }

    private void BrowseDefaultCover(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select default cover image",
            Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|All files (*.*)|*.*"
        };
        if (!string.IsNullOrEmpty(_defaultCoverBox.Text))
            dlg.InitialDirectory = System.IO.Path.GetDirectoryName(_defaultCoverBox.Text);
        if (dlg.ShowDialog() == DialogResult.OK)
            _defaultCoverBox.Text = dlg.FileName;
    }

    private void Save(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_clientIdBox.Text) || string.IsNullOrWhiteSpace(_clientSecretBox.Text))
        {
            _statusLabel.ForeColor = System.Drawing.Color.DarkRed;
            _statusLabel.Text = "Client ID and Secret are required.";
            return;
        }

        _config.SpotifyClientId = _clientIdBox.Text.Trim();
        _config.SpotifyClientSecret = _clientSecretBox.Text.Trim();
        _config.NowPlayingPath = _nowPlayingBox.Text.Trim();
        _config.OutputPath = _outputBox.Text.Trim();
        _config.DefaultCoverPath = _defaultCoverBox.Text.Trim();
        _config.CloseToTray = _closeToTrayBox.Checked;

        try
        {
            _config.Save();
            _statusLabel.ForeColor = System.Drawing.Color.DarkGreen;
            _statusLabel.Text = "Saved.";
            _onSaved();
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = System.Drawing.Color.DarkRed;
            _statusLabel.Text = $"Save failed: {ex.Message}";
        }
    }
}
