using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using FolderTools.Core;

namespace FolderTools.App
{
    internal sealed class SftpBookmarkEditorForm : Form
    {
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _path = new TextBox();
        private readonly TextBox _category = new TextBox();
        private readonly TextBox _fileMode = new TextBox();
        private readonly TextBox _directoryMode = new TextBox();
        private readonly TextBox _uid = new TextBox();
        private readonly TextBox _gid = new TextBox();
        public SftpBookmark Bookmark { get; private set; }

        public SftpBookmarkEditorForm(string path)
        {
            Bookmark = new SftpBookmark { Path = path };
            Text = "Закладка SFTP"; StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = false; MaximizeBox = false;
            ShowInTaskbar = false; ClientSize = new Size(480, 330); Font = new Font("Segoe UI", 9F);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 9 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            string[] labels = { "Название:", "Путь:", "Категория:", "chmod файл:", "chmod папка:", "UID (необязательно):", "GID (необязательно):" };
            Control[] controls = { _name, _path, _category, _fileMode, _directoryMode, _uid, _gid };
            for (int i = 0; i < labels.Length; i++) { layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); layout.Controls.Add(new Label { Text = labels[i], AutoSize = true, Anchor = AnchorStyles.Left }, 0, i); controls[i].Dock = DockStyle.Fill; layout.Controls.Add(controls[i], 1, i); }
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var save = new Button { Text = "Сохранить", Width = 100 }; save.Click += (s, e) => Save();
            var cancel = new Button { Text = "Отмена", Width = 90, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(save); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 8); layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
            _path.Text = path;
        }

        private void Save()
        {
            if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrWhiteSpace(_path.Text)) { Warn("Укажите название и удалённый путь."); return; }
            string? fileMode = ParseMode(_fileMode.Text, "chmod для файла");
            string? dirMode = ParseMode(_directoryMode.Text, "chmod для папки");
            if (_fileMode.Text.Trim().Length > 0 && fileMode == null || _directoryMode.Text.Trim().Length > 0 && dirMode == null) return;
            int? uid = ParseId(_uid.Text, "UID"); int? gid = ParseId(_gid.Text, "GID");
            if (_uid.Text.Trim().Length > 0 && !uid.HasValue || _gid.Text.Trim().Length > 0 && !gid.HasValue) return;
            Bookmark = new SftpBookmark { Name = _name.Text.Trim(), Path = _path.Text.Trim(), Category = _category.Text.Trim(), FileMode = fileMode, DirectoryMode = dirMode, UserId = uid, GroupId = gid };
            DialogResult = DialogResult.OK; Close();
        }
        private string? ParseMode(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!SftpPermissionRules.TryParseMode(value, out _)) { Warn(label + " должен быть восьмеричным числом 000–777."); return null; }
            return value.Trim();
        }
        private int? ParseId(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id < 0) { Warn(label + " должен быть неотрицательным числом."); return null; }
            return id;
        }
        private void Warn(string message) => MessageBox.Show(this, message, "Закладка SFTP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
