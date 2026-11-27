using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FolderTools.Core;

namespace FolderTools.App
{
    internal sealed class SftpRemoteFolderBrowserForm : Form
    {
        private readonly SftpDirectoryBrowserSession _session;
        private readonly TextBox _path = new TextBox();
        private readonly ListBox _directories = new ListBox();
        private readonly ComboBox _bookmarks = new ComboBox();
        private readonly Label _permissions = new Label();
        private readonly Button _up = new Button();
        private readonly Button _refresh = new Button();
        private readonly Button _select = new Button();
        private readonly List<SftpBookmark> _bookmarkList;

        public SftpRemoteFolderBrowserForm(SftpDirectoryBrowserSession session, string initialPath, IEnumerable<SftpBookmark>? bookmarks = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _bookmarkList = (bookmarks ?? Enumerable.Empty<SftpBookmark>()).Select(b => b.Clone()).ToList();
            SelectedPath = _session.HomeDirectory;
            Text = "Выбор удалённой папки SFTP";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            ClientSize = new Size(700, 500);
            MinimumSize = new Size(560, 400);
            Font = new Font("Segoe UI", 9F);
            BuildLayout();
            RefreshBookmarks();
            _path.Text = string.IsNullOrWhiteSpace(initialPath) ? SelectedPath : initialPath;
            LoadPath(_path.Text);
        }

        public string SelectedPath { get; private set; }
        public IReadOnlyList<SftpBookmark> Bookmarks => _bookmarkList;

        private void BuildLayout()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            _path.Dock = DockStyle.Fill;
            _path.TextChanged += (s, e) => UpdateButtons();
            _path.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LoadPath(_path.Text); } };
            _up.Text = "Вверх"; _up.Dock = DockStyle.Fill; _up.Click += (s, e) => GoUp();
            _refresh.Text = "Обновить"; _refresh.Dock = DockStyle.Fill; _refresh.Click += (s, e) => LoadPath(_path.Text);
            pathRow.Controls.Add(_path, 0, 0); pathRow.Controls.Add(_up, 1, 0); pathRow.Controls.Add(_refresh, 2, 0);
            layout.Controls.Add(pathRow, 0, 0);

            var bookmarkRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            bookmarkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bookmarkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            bookmarkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            _bookmarks.Dock = DockStyle.Fill; _bookmarks.DropDownStyle = ComboBoxStyle.DropDownList;
            _bookmarks.SelectedIndexChanged += (s, e) => { var b = _bookmarks.SelectedItem as BookmarkItem; if (b != null) LoadPath(b.Bookmark.Path); };
            var addBookmark = new Button { Text = "Добавить…", Dock = DockStyle.Fill };
            addBookmark.Click += (s, e) => AddBookmark();
            var removeBookmark = new Button { Text = "Удалить", Dock = DockStyle.Fill };
            removeBookmark.Click += (s, e) => RemoveBookmark();
            bookmarkRow.Controls.Add(_bookmarks, 0, 0); bookmarkRow.Controls.Add(addBookmark, 1, 0); bookmarkRow.Controls.Add(removeBookmark, 2, 0);
            layout.Controls.Add(bookmarkRow, 0, 1);

            _permissions.Dock = DockStyle.Fill;
            _permissions.TextAlign = ContentAlignment.MiddleLeft;
            _permissions.ForeColor = Color.FromArgb(70, 70, 70);
            layout.Controls.Add(_permissions, 0, 2);
            _directories.Dock = DockStyle.Fill; _directories.IntegralHeight = false;
            _directories.DoubleClick += (s, e) => EnterSelectedDirectory();
            layout.Controls.Add(_directories, 0, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            _select.Text = "Выбрать эту папку"; _select.Width = 150; _select.Height = 29; _select.Click += (s, e) => SelectCurrentPath();
            var cancel = new Button { Text = "Отмена", Width = 90, Height = 29, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(_select); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 4);
            Controls.Add(layout); AcceptButton = _select; CancelButton = cancel;
        }

        private void RefreshBookmarks()
        {
            string? selected = (_bookmarks.SelectedItem as BookmarkItem)?.Bookmark.Id;
            _bookmarks.Items.Clear();
            foreach (IGrouping<string, SftpBookmark> group in _bookmarkList.OrderBy(b => b.Category).ThenBy(b => b.Name).GroupBy(b => b.Category ?? string.Empty))
            foreach (SftpBookmark bookmark in group)
                _bookmarks.Items.Add(new BookmarkItem(bookmark, string.IsNullOrWhiteSpace(group.Key) ? bookmark.Name : group.Key + " / " + bookmark.Name));
            if (selected != null)
                for (int i = 0; i < _bookmarks.Items.Count; i++) if (((BookmarkItem)_bookmarks.Items[i]).Bookmark.Id == selected) _bookmarks.SelectedIndex = i;
        }

        private void AddBookmark()
        {
            using (var form = new SftpBookmarkEditorForm(Normalize(_path.Text)))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                _bookmarkList.Add(form.Bookmark);
                RefreshBookmarks();
                _bookmarks.SelectedIndex = _bookmarks.Items.Count - 1;
            }
        }

        private void RemoveBookmark()
        {
            BookmarkItem? item = _bookmarks.SelectedItem as BookmarkItem;
            if (item == null) return;
            _bookmarkList.RemoveAll(b => b.Id == item.Bookmark.Id);
            RefreshBookmarks();
        }

        private void LoadPath(string path)
        {
            path = Normalize(path);
            if (path.Length == 0) path = Normalize(_session.HomeDirectory);
            try
            {
                Cursor = Cursors.WaitCursor;
                _directories.Items.Clear();
                foreach (SftpRemoteDirectory directory in _session.ListDirectories(path)) _directories.Items.Add(directory);
                SelectedPath = path; _path.Text = path;
                _permissions.Text = _session.GetDirectorySummary(path);
                UpdateButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть удалённую папку:\n" + ex.Message, "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _path.Text = SelectedPath;
            }
            finally { Cursor = Cursors.Default; }
        }

        private void EnterSelectedDirectory()
        {
            SftpRemoteDirectory? selected = _directories.SelectedItem as SftpRemoteDirectory;
            if (selected != null) LoadPath(selected.FullName);
        }

        private void GoUp()
        {
            string current = Normalize(_path.Text);
            if (current.Length == 0) return;
            string trimmed = current.TrimEnd('/');
            int separator = trimmed.LastIndexOf('/');
            LoadPath(separator < 0 ? string.Empty : (separator == 0 ? "/" : trimmed.Substring(0, separator)));
        }

        private void SelectCurrentPath()
        {
            string path = Normalize(_path.Text);
            if (path.Length == 0) { MessageBox.Show(this, "Укажите удалённый путь.", "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            try { _session.ListDirectories(path); SelectedPath = path; DialogResult = DialogResult.OK; Close(); }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось выбрать удалённую папку:\n" + ex.Message, "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void UpdateButtons() { _up.Enabled = Normalize(_path.Text).Length > 0 && Normalize(_path.Text) != "/"; _select.Enabled = !string.IsNullOrWhiteSpace(_path.Text); }
        private static string Normalize(string path)
        {
            string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
            while (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal)) normalized = normalized.Substring(0, normalized.Length - 1);
            return normalized;
        }
        private sealed class BookmarkItem
        {
            public BookmarkItem(SftpBookmark bookmark, string label) { Bookmark = bookmark; Label = label; }
            public SftpBookmark Bookmark { get; }
            private string Label { get; }
            public override string ToString() => Label;
        }
    }
}
