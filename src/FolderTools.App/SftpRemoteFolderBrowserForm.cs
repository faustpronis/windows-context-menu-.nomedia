using System;
using System.Drawing;
using System.Windows.Forms;

namespace FolderTools.App
{
    internal sealed class SftpRemoteFolderBrowserForm : Form
    {
        private readonly SftpDirectoryBrowserSession _session;
        private readonly TextBox _path = new TextBox();
        private readonly ListBox _directories = new ListBox();
        private readonly Button _up = new Button();
        private readonly Button _refresh = new Button();
        private readonly Button _select = new Button();

        public SftpRemoteFolderBrowserForm(SftpDirectoryBrowserSession session, string initialPath)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            SelectedPath = _session.HomeDirectory;

            Text = "Выбор удалённой папки SFTP";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 420);
            Font = new Font("Segoe UI", 9F);

            BuildLayout();
            _path.Text = string.IsNullOrWhiteSpace(initialPath) ? SelectedPath : initialPath;
            LoadPath(_path.Text);
        }

        public string SelectedPath { get; private set; }

        private void BuildLayout()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 3
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            _path.Dock = DockStyle.Fill;
            _path.TextChanged += (s, e) => UpdateButtons();
            _path.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    LoadPath(_path.Text);
                }
            };
            _up.Text = "Вверх";
            _up.Dock = DockStyle.Fill;
            _up.Click += (s, e) => GoUp();
            _refresh.Text = "Обновить";
            _refresh.Dock = DockStyle.Fill;
            _refresh.Click += (s, e) => LoadPath(_path.Text);
            pathRow.Controls.Add(_path, 0, 0);
            pathRow.Controls.Add(_up, 1, 0);
            pathRow.Controls.Add(_refresh, 2, 0);
            layout.Controls.Add(pathRow, 0, 0);

            _directories.Dock = DockStyle.Fill;
            _directories.IntegralHeight = false;
            _directories.DoubleClick += (s, e) => EnterSelectedDirectory();
            layout.Controls.Add(_directories, 0, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            _select.Text = "Выбрать эту папку";
            _select.Width = 150;
            _select.Height = 29;
            _select.Click += (s, e) => SelectCurrentPath();
            var cancel = new Button { Text = "Отмена", Width = 90, Height = 29, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(_select);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout);
            AcceptButton = _select;
            CancelButton = cancel;
        }

        private void LoadPath(string path)
        {
            path = Normalize(path);
            if (path.Length == 0)
            {
                path = Normalize(_session.HomeDirectory);
            }
            try
            {
                Cursor = Cursors.WaitCursor;
                _directories.Items.Clear();
                foreach (SftpRemoteDirectory directory in _session.ListDirectories(path))
                {
                    _directories.Items.Add(directory);
                }

                SelectedPath = path;
                _path.Text = path;
                UpdateButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть удалённую папку:\n" + ex.Message,
                    "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _path.Text = SelectedPath;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void EnterSelectedDirectory()
        {
            SftpRemoteDirectory? selected = _directories.SelectedItem as SftpRemoteDirectory;
            if (selected != null)
            {
                LoadPath(selected.FullName);
            }
        }

        private void GoUp()
        {
            string current = Normalize(_path.Text);
            if (current.Length == 0)
            {
                return;
            }

            string trimmed = current.TrimEnd('/');
            int separator = trimmed.LastIndexOf('/');
            string parent = separator < 0 ? string.Empty : (separator == 0 ? "/" : trimmed.Substring(0, separator));
            LoadPath(parent);
        }

        private void SelectCurrentPath()
        {
            string path = Normalize(_path.Text);
            if (path.Length == 0)
            {
                MessageBox.Show(this, "Укажите удалённый путь.", "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                // Loading the path also verifies that it exists and is a directory.
                _session.ListDirectories(path);
                SelectedPath = path;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось выбрать удалённую папку:\n" + ex.Message,
                    "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateButtons()
        {
            _up.Enabled = Normalize(_path.Text).Length > 0 && Normalize(_path.Text) != "/";
            _select.Enabled = !string.IsNullOrWhiteSpace(_path.Text);
        }

        private static string Normalize(string path)
        {
            string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
            while (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }
            return normalized;
        }
    }
}
