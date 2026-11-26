using System;
using System.Drawing;
using System.Windows.Forms;

namespace FolderTools.App
{
    internal sealed class MasterPasswordEntryForm : Form
    {
        private readonly TextBox _password = new TextBox();

        private MasterPasswordEntryForm(string? hint, string? message)
        {
            Text = "FolderTools — мастер-пароль";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(470, 180);
            Font = new Font("Segoe UI", 9F);

            var description = new Label
            {
                Dock = DockStyle.Top,
                Height = 72,
                Text = (message ?? "Введите мастер-пароль, чтобы расшифровать переносимые учётные данные SFTP.") +
                       (string.IsNullOrWhiteSpace(hint) ? string.Empty : "\r\nПодсказка: " + hint),
                Padding = new Padding(0, 2, 0, 6)
            };
            _password.Dock = DockStyle.Top;
            _password.UseSystemPasswordChar = true;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var ok = new Button { Text = "Разблокировать", Width = 125, Height = 28, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Отмена", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            Controls.Add(_password);
            Controls.Add(buttons);
            Controls.Add(description);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public static string? Prompt(IWin32Window? owner, string? hint, string? message = null)
        {
            using (var dialog = new MasterPasswordEntryForm(hint, message))
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._password.Text : null;
        }
    }

    internal sealed class MasterPasswordSetupForm : Form
    {
        private readonly TextBox _current = new TextBox();
        private readonly TextBox _newPassword = new TextBox();
        private readonly TextBox _confirmation = new TextBox();
        private readonly TextBox _hint = new TextBox();

        public MasterPasswordSetupForm(string? existingHint, bool hasEncryptedCredentials)
        {
            Text = "FolderTools — мастер-пароль SFTP";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(540, hasEncryptedCredentials ? 235 : 205);
            Font = new Font("Segoe UI", 9F);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = hasEncryptedCredentials ? 6 : 5 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int row = 0;
            if (hasEncryptedCredentials)
            {
                _current.UseSystemPasswordChar = true;
                Add(layout, row++, "Текущий мастер-пароль:", _current);
            }
            _newPassword.UseSystemPasswordChar = true;
            Add(layout, row++, "Новый мастер-пароль:", _newPassword);
            _confirmation.UseSystemPasswordChar = true;
            Add(layout, row++, "Повторите пароль:", _confirmation);
            _hint.Text = existingHint ?? string.Empty;
            Add(layout, row++, "Подсказка (необязательно):", _hint);
            var note = new Label
            {
                Text = "Пароль будет сохранён только в Windows Credential Manager этого компьютера. " +
                       "Зашифрованные SFTP-пароли останутся в переносимом файле настроек.",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(75, 75, 75)
            };
            layout.Controls.Add(note, 0, row);
            layout.SetColumnSpan(note, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 4, 8, 0) };
            var ok = new Button { Text = "Сохранить", Width = 100, Height = 28, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Отмена", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            Controls.Add(layout);
            Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public string CurrentPassword => _current.Text;
        public string NewPassword => _newPassword.Text;
        public string Confirmation => _confirmation.Text;
        public string Hint => _hint.Text.Trim();

        private static void Add(TableLayoutPanel layout, int row, string label, Control control)
        {
            control.Dock = DockStyle.Fill;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            layout.Controls.Add(control, 1, row);
        }
    }
}
