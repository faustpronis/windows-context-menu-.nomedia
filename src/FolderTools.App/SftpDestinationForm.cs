using System;
using System.Drawing;
using System.Windows.Forms;
using FolderTools.Core;

namespace FolderTools.App
{
    internal sealed class SftpDestinationForm : Form
    {
        private readonly Destination _destination;
        private readonly Destination _editedDestination;
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _host = new TextBox();
        private readonly NumericUpDown _port = new NumericUpDown();
        private readonly TextBox _remotePath = new TextBox();
        private readonly TextBox _username = new TextBox();
        private readonly TextBox _password = new TextBox();
        private readonly Label _credentialStatus = new Label();
        private readonly Label _hostKeyStatus = new Label();
        private SftpCredentialLookup _credentials;

        public SftpDestinationForm(Destination destination)
        {
            _destination = destination ?? throw new ArgumentNullException(nameof(destination));
            _editedDestination = destination.Clone();
            _credentials = LookupCredentials();

            Text = "FolderTools — SFTP-получатель";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(610, 350);
            Font = new Font("Segoe UI", 9F);

            BuildLayout();
            LoadValues();
        }

        private void BuildLayout()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 2,
                RowCount = 10,
                AutoSize = false
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 6; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            }
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            AddRow(layout, 0, "Название в меню:", _name);
            AddRow(layout, 1, "Сервер:", _host);
            _port.Minimum = 1;
            _port.Maximum = 65535;
            _port.Value = 22;
            _port.Width = 110;
            AddRow(layout, 2, "Порт:", _port);
            AddRow(layout, 3, "Удалённая папка:", _remotePath);
            AddRow(layout, 4, "Пользователь:", _username);
            _password.UseSystemPasswordChar = true;
            AddRow(layout, 5, "Пароль:", _password);

            _credentialStatus.Dock = DockStyle.Fill;
            _credentialStatus.TextAlign = ContentAlignment.MiddleLeft;
            _credentialStatus.ForeColor = Color.FromArgb(80, 80, 80);
            layout.Controls.Add(new Label { Text = "Хранение пароля:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 6);
            layout.Controls.Add(_credentialStatus, 1, 6);

            _hostKeyStatus.Dock = DockStyle.Fill;
            _hostKeyStatus.TextAlign = ContentAlignment.MiddleLeft;
            _hostKeyStatus.ForeColor = Color.FromArgb(80, 80, 80);
            layout.Controls.Add(new Label { Text = "Ключ сервера:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 7);
            var hostKeyRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            hostKeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            hostKeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            var forgetHostKey = new Button { Text = "Сбросить доверие…", Dock = DockStyle.Fill };
            forgetHostKey.Click += (s, e) =>
            {
                _editedDestination.SftpHostKeyFingerprint = null;
                _hostKeyStatus.Text = "Не проверен — будет запрошено подтверждение при тесте.";
                _hostKeyStatus.ForeColor = Color.DarkRed;
            };
            hostKeyRow.Controls.Add(_hostKeyStatus, 0, 0);
            hostKeyRow.Controls.Add(forgetHostKey, 1, 0);
            layout.Controls.Add(hostKeyRow, 1, 7);

            var note = new Label
            {
                Dock = DockStyle.Fill,
                Text = "При первом тесте программа покажет SHA-256 fingerprint SSH-сервера. " +
                       "Сверьте его с администратором сервера перед подтверждением. Пароль хранится " +
                       "в зашифрованном виде для текущей учётной записи Windows.",
                ForeColor = Color.FromArgb(70, 70, 70),
                AutoSize = false
            };
            layout.Controls.Add(note, 0, 8);
            layout.SetColumnSpan(note, 2);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            var save = new Button { Text = "Сохранить", Width = 100, Height = 29 };
            save.Click += (s, e) => SaveAndClose();
            var cancel = new Button { Text = "Отмена", Width = 90, Height = 29, DialogResult = DialogResult.Cancel };
            var test = new Button { Text = "Проверить соединение", Width = 160, Height = 29 };
            test.Click += (s, e) => TestConnection();
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(test);
            layout.Controls.Add(buttons, 0, 9);
            layout.SetColumnSpan(buttons, 2);

            Controls.Add(layout);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private static void AddRow(TableLayoutPanel layout, int row, string label, Control input)
        {
            input.Dock = DockStyle.Fill;
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            layout.Controls.Add(input, 1, row);
        }

        private void LoadValues()
        {
            _name.Text = _editedDestination.Name;
            _host.Text = _editedDestination.SftpHost ?? string.Empty;
            _port.Value = _editedDestination.SftpPort < 1 || _editedDestination.SftpPort > 65535 ? 22 : _editedDestination.SftpPort;
            _remotePath.Text = _editedDestination.SftpRemotePath ?? string.Empty;
            _username.Text = _credentials.State == SftpCredentialState.Available ? _credentials.Username ?? string.Empty : string.Empty;
            _hostKeyStatus.Text = string.IsNullOrWhiteSpace(_editedDestination.SftpHostKeyFingerprint)
                ? "Не проверен — будет запрошено подтверждение при тесте."
                : _editedDestination.SftpHostKeyFingerprint;
            UpdateCredentialStatus();
        }

        private SftpCredentialLookup LookupCredentials()
        {
            try
            {
                return SftpCredentialStore.Get(_destination.Id);
            }
            catch (Exception)
            {
                return SftpCredentialLookup.NeedsReentry();
            }
        }

        private void UpdateCredentialStatus()
        {
            switch (_credentials.State)
            {
                case SftpCredentialState.Available:
                    _credentialStatus.Text = "Сохранён для текущей учётной записи Windows. Пустое поле пароля оставит его без изменений.";
                    _credentialStatus.ForeColor = Color.FromArgb(0, 110, 0);
                    break;
                case SftpCredentialState.NeedsReentry:
                    _credentialStatus.Text = "Пароль нельзя расшифровать. Введите его заново для этой учётной записи/компьютера.";
                    _credentialStatus.ForeColor = Color.DarkRed;
                    break;
                default:
                    _credentialStatus.Text = "Не задан. Введите пароль для сохранения подключения.";
                    _credentialStatus.ForeColor = Color.DarkRed;
                    break;
            }
        }

        private Destination GetEditedDestination()
        {
            string newHost = _host.Text.Trim();
            int newPort = Decimal.ToInt32(_port.Value);
            if (!string.Equals(_editedDestination.SftpHost, newHost, StringComparison.OrdinalIgnoreCase)
                || _editedDestination.SftpPort != newPort)
            {
                _editedDestination.SftpHostKeyFingerprint = null;
            }

            _editedDestination.Type = DestinationType.Sftp;
            _editedDestination.Name = _name.Text.Trim();
            _editedDestination.Path = string.Empty;
            _editedDestination.SftpHost = newHost;
            _editedDestination.SftpPort = newPort;
            _editedDestination.SftpRemotePath = _remotePath.Text.Trim();
            return _editedDestination;
        }

        private void TestConnection()
        {
            Destination edited = GetEditedDestination();
            string username = _username.Text.Trim();
            string password = _password.Text;
            if (password.Length == 0 && _credentials.State == SftpCredentialState.Available)
            {
                password = _credentials.Password ?? string.Empty;
            }

            try
            {
                SftpTransferService.TestConnection(edited, username, password, ConfirmUnknownHostKey);
                _hostKeyStatus.Text = edited.SftpHostKeyFingerprint ?? "Проверен";
                _hostKeyStatus.ForeColor = Color.FromArgb(0, 110, 0);
                MessageBox.Show(this, "Соединение установлено, удалённая папка доступна.", "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось проверить SFTP-подключение:\n" + ex.Message, "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private bool ConfirmUnknownHostKey(string fingerprint)
        {
            DialogResult result = MessageBox.Show(
                this,
                "У этого сервера ещё не сохранён ключ. Сверьте fingerprint с администратором сервера.\n\n" +
                "Сервер: " + _host.Text.Trim() + ":" + Decimal.ToInt32(_port.Value) + "\n" +
                "SHA-256: " + fingerprint + "\n\nДоверять этому ключу и сохранить его?",
                "Проверка SSH-ключа сервера",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            return result == DialogResult.Yes;
        }

        private void SaveAndClose()
        {
            Destination edited = GetEditedDestination();
            if (string.IsNullOrWhiteSpace(edited.Name) || string.IsNullOrWhiteSpace(edited.SftpHost) ||
                string.IsNullOrWhiteSpace(edited.SftpRemotePath) || string.IsNullOrWhiteSpace(_username.Text))
            {
                MessageBox.Show(this, "Укажите название, сервер, удалённую папку и имя пользователя.", "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool usernameChanged = _credentials.State == SftpCredentialState.Available
                && !string.Equals(_username.Text.Trim(), _credentials.Username, StringComparison.Ordinal);
            if (_password.Text.Length > 0)
            {
                try
                {
                    SftpCredentialStore.Save(edited.Id, _username.Text.Trim(), _password.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Не удалось сохранить пароль:\n" + ex.Message, "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else if (_credentials.State != SftpCredentialState.Available || usernameChanged)
            {
                MessageBox.Show(this, "Введите пароль для текущего имени пользователя. Без него сохранённые учётные данные нельзя безопасно изменить.", "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_destination.Patterns.Count == 0)
            {
                _destination.Patterns.Add("*");
                _destination.Patterns.Add(ExtensionPattern.FolderToken);
            }

            _destination.Name = edited.Name;
            _destination.Path = edited.Path;
            _destination.Type = edited.Type;
            _destination.SftpHost = edited.SftpHost;
            _destination.SftpPort = edited.SftpPort;
            _destination.SftpRemotePath = edited.SftpRemotePath;
            _destination.SftpHostKeyFingerprint = edited.SftpHostKeyFingerprint;

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
