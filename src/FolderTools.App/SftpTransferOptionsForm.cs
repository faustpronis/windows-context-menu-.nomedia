using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using FolderTools.Core;

namespace FolderTools.App
{
    internal sealed class SftpTransferOptionsForm : Form
    {
        private readonly ComboBox _conflict = new ComboBox();
        private readonly ComboBox _preset = new ComboBox();
        private readonly CheckBox _apply = new CheckBox();
        private readonly TextBox _fileMode = new TextBox { Text = "0644" };
        private readonly TextBox _directoryMode = new TextBox { Text = "0755" };
        private readonly TextBox _uid = new TextBox();
        private readonly TextBox _gid = new TextBox();
        public SftpTransferOptions Options { get; private set; } = new SftpTransferOptions();

        private SftpTransferOptionsForm(SftpTransferOptions initial, System.Collections.Generic.IEnumerable<SftpBookmark>? bookmarks)
        {
            Text = "Параметры SFTP-передачи"; StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(500, 310); Font = new Font("Segoe UI", 9F);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 9 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Add(layout, 0, "При конфликте:", _conflict);
            _conflict.DropDownStyle = ComboBoxStyle.DropDownList;
            _conflict.Items.Add("Заменить все"); _conflict.Items.Add("Пропустить имеющиеся"); _conflict.Items.Add("Заменить, сохранив прежнее");
            _conflict.SelectedIndex = (int)initial.ConflictPolicy;
            _preset.DropDownStyle = ComboBoxStyle.DropDownList; _preset.Items.Add("Без закладки");
            foreach (SftpBookmark b in bookmarks ?? Array.Empty<SftpBookmark>()) _preset.Items.Add(new PresetItem(b));
            _preset.SelectedIndex = 0;
            _preset.SelectedIndexChanged += (s, e) => ApplyPreset();
            Add(layout, 1, "Закладка-пресет:", _preset);
            _apply.Text = "Задать chmod для отправки"; _apply.AutoSize = true; layout.Controls.Add(_apply, 1, 2);
            Add(layout, 3, "chmod для файлов:", _fileMode); Add(layout, 4, "chmod для папок:", _directoryMode);
            Add(layout, 5, "UID (необязательно):", _uid); Add(layout, 6, "GID (необязательно):", _gid);
            var note = new Label { Text = "Без UID/GID сохраняются права и владелец по умолчанию сервера. Для Move источник удаляется только после успешной передачи и применения метаданных.", Dock = DockStyle.Fill, ForeColor = Color.DimGray };
            layout.Controls.Add(note, 0, 7); layout.SetColumnSpan(note, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var ok = new Button { Text = "Продолжить", Width = 110 }; ok.Click += (s, e) => Save();
            var cancel = new Button { Text = "Отмена", Width = 90, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 8); layout.SetColumnSpan(buttons, 2);
            for (int i = 0; i < 7; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            Controls.Add(layout); AcceptButton = ok; CancelButton = cancel;
            _apply.Checked = initial.ApplyExplicitPermissions;
            _fileMode.Text = initial.FileMode; _directoryMode.Text = initial.DirectoryMode;
            _uid.Text = initial.UserId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            _gid.Text = initial.GroupId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }

        public static bool TryShow(IWin32Window? owner, SftpTransferOptions initial, System.Collections.Generic.IEnumerable<SftpBookmark>? bookmarks, out SftpTransferOptions options)
        {
            using (var form = new SftpTransferOptionsForm(initial, bookmarks))
            {
                bool accepted = (owner == null ? form.ShowDialog() : form.ShowDialog(owner)) == DialogResult.OK;
                options = form.Options; return accepted;
            }
        }
        private static void Add(TableLayoutPanel panel, int row, string label, Control control)
        { control.Dock = DockStyle.Fill; panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row); panel.Controls.Add(control, 1, row); }
        private void Save()
        {
            if (_apply.Checked && (!SftpPermissionRules.TryParseMode(_fileMode.Text, out _) || !SftpPermissionRules.TryParseMode(_directoryMode.Text, out _)))
            { MessageBox.Show(this, "chmod должен быть восьмеричным числом 000–777 (например 0644/0755).", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            int? uid = ParseId(_uid.Text); int? gid = ParseId(_gid.Text);
            if (_uid.Text.Trim().Length > 0 && !uid.HasValue || _gid.Text.Trim().Length > 0 && !gid.HasValue)
            { MessageBox.Show(this, "UID и GID должны быть неотрицательными целыми числами.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Options = new SftpTransferOptions { ConflictPolicy = (SftpConflictPolicy)_conflict.SelectedIndex, ApplyExplicitPermissions = _apply.Checked, FileMode = _fileMode.Text.Trim(), DirectoryMode = _directoryMode.Text.Trim(), UserId = uid, GroupId = gid };
            DialogResult = DialogResult.OK; Close();
        }
        private void ApplyPreset()
        {
            PresetItem? item = _preset.SelectedItem as PresetItem;
            if (item == null) return;
            SftpBookmark b = item.Bookmark;
            if (!string.IsNullOrWhiteSpace(b.FileMode)) { _fileMode.Text = b.FileMode; _apply.Checked = true; }
            if (!string.IsNullOrWhiteSpace(b.DirectoryMode)) { _directoryMode.Text = b.DirectoryMode; _apply.Checked = true; }
            _uid.Text = b.UserId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            _gid.Text = b.GroupId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }
        private static int? ParseId(string value) => int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id >= 0 ? (int?)id : null;
        private sealed class PresetItem
        {
            public PresetItem(SftpBookmark bookmark) { Bookmark = bookmark; }
            public SftpBookmark Bookmark { get; }
            public override string ToString() => string.IsNullOrWhiteSpace(Bookmark.Category) ? Bookmark.Name : Bookmark.Category + " / " + Bookmark.Name;
        }
    }
}
