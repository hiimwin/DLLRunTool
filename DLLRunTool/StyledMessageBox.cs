using System.Drawing;

namespace DLLRunTool;

/// <summary>Styled confirmation dialog matching MCP dark/gold theme.</summary>
internal sealed class StyledMessageBox : Form
{
    private readonly Label _messageLabel;
    private DialogResult _result = DialogResult.Cancel;

    private StyledMessageBox(string title, string message, IReadOnlyList<StyledButton> buttons, MessageBoxIcon icon)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(480, 220);
        BackColor = Color.FromArgb(34, 35, 38);
        ForeColor = Color.FromArgb(232, 234, 237);
        Font = new Font("Segoe UI", 10F);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 56,
            BackColor = Color.FromArgb(42, 43, 47),
            Padding = new Padding(16, 12, 16, 8)
        };

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 12F),
            ForeColor = Color.FromArgb(255, 224, 130),
            TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.Add(titleLabel);

        _messageLabel = new Label
        {
            Text = message,
            AutoSize = false,
            Location = new Point(20, 68),
            Size = new Size(440, 96),
            ForeColor = Color.FromArgb(200, 203, 208),
            Font = new Font("Segoe UI", 10F)
        };

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 8, 16, 12),
            BackColor = Color.FromArgb(34, 35, 38),
            WrapContents = false
        };

        foreach (var btn in buttons.AsEnumerable().Reverse())
        {
            var control = CreateButton(btn);
            footer.Controls.Add(control);
        }

        Controls.Add(footer);
        Controls.Add(_messageLabel);
        Controls.Add(header);

        AcceptButton = footer.Controls.OfType<Button>().LastOrDefault(b => b.DialogResult == DialogResult.Yes)
                       ?? footer.Controls.OfType<Button>().FirstOrDefault();
        CancelButton = footer.Controls.OfType<Button>().FirstOrDefault(b => b.DialogResult == DialogResult.Cancel);

        Load += (_, _) => WindowChrome.ApplyRoundedCorners(this);
    }

    private Button CreateButton(StyledButton spec)
    {
        var btn = new Button
        {
            Text = spec.Text,
            AutoSize = true,
            MinimumSize = new Size(96, 34),
            Margin = new Padding(8, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            DialogResult = spec.Result,
            Cursor = Cursors.Hand,
            Padding = new Padding(14, 6, 14, 6),
            Font = new Font("Segoe UI Semibold", 9.5F)
        };
        btn.FlatAppearance.BorderSize = 1;

        if (spec.IsPrimary)
        {
            btn.BackColor = Color.FromArgb(249, 171, 0);
            btn.ForeColor = Color.FromArgb(32, 33, 36);
            btn.FlatAppearance.BorderColor = Color.FromArgb(255, 213, 79);
        }
        else if (spec.IsDanger)
        {
            btn.BackColor = Color.FromArgb(60, 32, 32);
            btn.ForeColor = Color.FromArgb(242, 139, 130);
            btn.FlatAppearance.BorderColor = Color.FromArgb(120, 60, 55);
        }
        else
        {
            btn.BackColor = Color.FromArgb(45, 46, 50);
            btn.ForeColor = Color.FromArgb(200, 203, 208);
            btn.FlatAppearance.BorderColor = Color.FromArgb(70, 72, 78);
        }

        btn.Click += (_, _) =>
        {
            _result = spec.Result;
            Close();
        };

        return btn;
    }

    public static DialogResult Show(
        IWin32Window? owner,
        string message,
        string title,
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.Information)
    {
        var specs = buttons switch
        {
            MessageBoxButtons.YesNo => new[]
            {
                new StyledButton("Không", DialogResult.No),
                new StyledButton("Có", DialogResult.Yes, IsPrimary: true)
            },
            MessageBoxButtons.YesNoCancel => new[]
            {
                new StyledButton("Hủy", DialogResult.Cancel),
                new StyledButton("Không", DialogResult.No),
                new StyledButton("Có", DialogResult.Yes, IsPrimary: true)
            },
            MessageBoxButtons.OK => new[] { new StyledButton("OK", DialogResult.OK, IsPrimary: true) },
            _ => new[] { new StyledButton("OK", DialogResult.OK, IsPrimary: true) }
        };

        using var dlg = new StyledMessageBox(title, message, specs, icon);
        return owner == null ? dlg.ShowDialog() : dlg.ShowDialog(owner);
    }

    private sealed record StyledButton(string Text, DialogResult Result, bool IsPrimary = false, bool IsDanger = false);
}
