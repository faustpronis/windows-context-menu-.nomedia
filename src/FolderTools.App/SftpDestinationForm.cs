using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
        private readonly TextBox _fallbacks = new TextBox();
        private readonly TextBox _remotePath = new TextBox();
        private readonly TextBox _username = new TextBox();
        private readonly TextBox _password = new TextBox();
        private readonly TextBox _chmodExtensions = new TextBox();
        private readonly TextBox _chmodMode = new TextBox();
        private readonly Label _credentialStatus = new Label();
        private readonly Label _hostKeyStatus = new Label();
        private SftpCredentialLookup _credentials;
        private bool _remoteBrowseSucceeded;
        private readonly string? _masterPasswordOverride;
        private readonly string? _masterPasswordHint;
        private readonly bool _hasExistingPortableCredentials;

        public string? MasterPasswordUsed { get; private set; }

        public SftpDestinationForm(Destination destination, string? masterPasswordOverride = null, string? masterPasswordHint = null, bool hasExistingPortableCredentials = false)
        {
            _destination = destination ?? throw new ArgumentNullException(nameof(destination));
            _editedDestination = destination.Clone();
            _masterPasswordOverride = masterPasswordOverride;
            _masterPasswordHint = masterPasswordHint;
            _hasExistingPortableCredentials = hasExistingPortableCredentials;
            _credentials = LookupCredentials();

            Text = "FolderTools — SFTP-получатель";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(670, 610);
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
                RowCount = 13,
                AutoSize = false
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            }
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            for (int i = 0; i < 3; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            }
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            AddRow(layout, 0, "Название в меню:", _name);
            AddRow(layout, 1, "Основной сервер:", _host);
            _port.Minimum = 1;
            _port.Maximum = 65535;
            _port.Value = 22;
            _port.Width = 110;
            AddRow(layout, 2, "Порт:", _port);

            _fallbacks.Multiline = true;
            _fallbacks.AcceptsReturn = true;
            _fallbacks.ScrollBars = ScrollBars.Vertical;
            _fallbacks.Dock = DockStyle.Fill;
            layout.Controls.Add(new Label { Text = "Резервные адреса:", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(3, 7, 3, 3) }, 0, 3);
            layout.Controls.Add(_fallbacks, 1, 3);

            var remotePathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            remotePathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            remotePathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            _remotePath.Dock = DockStyle.Fill;
            var browseRemote = new Button { Text = "Обзор…", Dock = DockStyle.Fill };
            browseRemote.Click += (s, e) => BrowseRemoteFolder();
            remotePathRow.Controls.Add(_remotePath, 0, 0);
            remotePathRow.Controls.Add(browseRemote, 1, 0);
            layout.Controls.Add(new Label { Text = "Удалённая папка:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
            layout.Controls.Add(remotePathRow, 1, 4);
            AddRow(layout, 5, "Пользователь:", _username);
            _password.UseSystemPasswordChar = true;
            AddRow(layout, 6, "Пароль:", _password);

            _credentialStatus.Dock = DockStyle.Fill;
            _credentialStatus.TextAlign = ContentAlignment.MiddleLeft;
            _credentialStatus.ForeColor = Color.FromArgb(80, 80, 80);
            layout.Controls.Add(new Label { Text = "Хранение пароля:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 7);
            layout.Controls.Add(_credentialStatus, 1, 7);

            _hostKeyStatus.Dock = DockStyle.Fill;
            _hostKeyStatus.TextAlign = ContentAlignment.MiddleLeft;
            _hostKeyStatus.ForeColor = Color.FromArgb(80, 80, 80);
            layout.Controls.Add(new Label { Text = "Ключи серверов:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 8);
            var hostKeyRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            hostKeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            hostKeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
            var forgetHostKey = new Button { Text = "Сбросить доверие…", Dock = DockStyle.Fill };
            forgetHostKey.Click += (s, e) =>
            {
                _editedDestination.SftpHostKeyFingerprint = null;
                foreach (SftpEndpoint endpoint in _editedDestination.SftpFallbacks)
                {
                    endpoint.HostKeyFingerprint = null;
                }
                _hostKeyStatus.Text = "Ключи будут подтверждаться при следующем подключении.";
                _hostKeyStatus.ForeColor = Color.DarkRed;
            };
            hostKeyRow.Controls.Add(_hostKeyStatus, 0, 0);
            hostKeyRow.Controls.Add(forgetHostKey, 1, 0);
            layout.Controls.Add(hostKeyRow, 1, 8);

            _chmodExtensions.Multiline = true;
            _chmodExtensions.Dock = DockStyle.Fill;
            AddRow(layout, 9, "chmod расширения:", _chmodExtensions);
            AddRow(layout, 10, "chmod режим (octal):", _chmodMode);

            var note = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Резервные адреса — по одному на строке: host или host:port; без порта используется основной порт. " +
                       "При отправке подключения проверяются параллельно, используется первый доступный с доверенным SSH-ключом и доступом к папке.\r\n" +
                       "Для chmod укажите расширения через запятую (например, .sh, .py) и режим 0644/0755. Права меняются после проверки загрузки, до публикации файла.",
                ForeColor = Color.FromArgb(70, 70, 70),
                AutoSize = false
            };
            layout.Controls.Add(note, 0, 11);
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
            layout.Controls.Add(buttons, 0, 12);
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
            int primaryPort = Decimal.ToInt32(_port.Value);
            _fallbacks.Lines = (_editedDestination.SftpFallbacks ?? new List<SftpEndpoint>())
                .Where(e => !string.IsNullOrWhiteSpace(e.Host))
                .Select(e => FormatEndpoint(e, primaryPort))
                .ToArray();
            _remotePath.Text = _editedDestination.SftpRemotePath ?? string.Empty;
            _username.Text = _credentials.State == SftpCredentialState.Available
                ? _credentials.Username ?? string.Empty
                : _editedDestination.SftpUsername ?? string.Empty;
            _chmodExtensions.Text = string.Join(", ", _editedDestination.SftpChmodExtensions ?? new List<string>());
            _chmodMode.Text = string.IsNullOrWhiteSpace(_editedDestination.SftpChmodMode) ? "0644" : _editedDestination.SftpChmodMode;
            _hostKeyStatus.Text = string.IsNullOrWhiteSpace(_editedDestination.SftpHostKeyFingerprint)
                ? "Основной ключ не проверен; резервные ключи закрепляются отдельно."
                : "Основной: " + _editedDestination.SftpHostKeyFingerprint;
            UpdateCredentialStatus();
        }

        private static string FormatEndpoint(SftpEndpoint endpoint, int primaryPort)
        {
            string host = endpoint.Host ?? string.Empty;
            if (host.IndexOf(':') >= 0 && !host.StartsWith("[", StringComparison.Ordinal))
            {
                host = "[" + host + "]";
            }

            return endpoint.Port == primaryPort ? host : host + ":" + endpoint.Port;
        }

        private SftpCredentialLookup LookupCredentials()
        {
            try
            {
                return SftpCredentialStore.GetCached(_destination);
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
                    if (!string.IsNullOrWhiteSpace(_editedDestination.SftpPasswordEncrypted))
                    {
                        _credentialStatus.Text = "Зашифрован в конфиге; локальный кэш будет создан при первом разблокировании.";
                        _credentialStatus.ForeColor = Color.FromArgb(0, 90, 130);
                    }
                    else
                    {
                        _credentialStatus.Text = "Старый пароль не найден в локальном хранилище. Введите его для миграции.";
                        _credentialStatus.ForeColor = Color.DarkRed;
                    }
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
            if (!string.Equals(_editedDestination.SftpHost, newHost, StringComparison.OrdinalIgnoreCase) ||
                _editedDestination.SftpPort != newPort)
            {
                _editedDestination.SftpHostKeyFingerprint = null;
            }

            _editedDestination.Type = DestinationType.Sftp;
            _editedDestination.Name = _name.Text.Trim();
            _editedDestination.Path = string.Empty;
            _editedDestination.SftpHost = newHost;
            _editedDestination.SftpPort = newPort;
            _editedDestination.SftpRemotePath = _remotePath.Text.Trim();
            _editedDestination.SftpFallbacks = ParseFallbacks(_fallbacks.Lines, newPort, _editedDestination.SftpFallbacks);
            _editedDestination.SftpChmodExtensions = SftpPermissionRules.ParseExtensions(_chmodExtensions.Text).ToList();
            if (!SftpPermissionRules.TryParseMode(_chmodMode.Text, out _))
            {
                throw new FormatException("Режим chmod должен быть восьмеричным числом 000–777, например 0644 или 0755.");
            }
            _editedDestination.SftpChmodMode = _chmodMode.Text.Trim();
            return _editedDestination;
        }

        private static List<SftpEndpoint> ParseFallbacks(IEnumerable<string> lines, int defaultPort, IEnumerable<SftpEndpoint>? previous)
        {
            var result = new List<SftpEndpoint>();
            var old = (previous ?? Enumerable.Empty<SftpEndpoint>()).ToList();
            foreach (string raw in lines ?? Enumerable.Empty<string>())
            {
                string line = (raw ?? string.Empty).Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                string host;
                int port = defaultPort;
                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    if (close < 2)
                    {
                        throw new FormatException("Некорректный IPv6 резервный адрес: " + line);
                    }
                    host = line.Substring(1, close - 1);
                    string suffix = line.Substring(close + 1);
                    if (suffix.Length > 0)
                    {
                        if (suffix[0] != ':' || !int.TryParse(suffix.Substring(1), out port))
                        {
                            throw new FormatException("Некорректный порт в резервном адресе: " + line);
                        }
                    }
                }
                else
                {
                    int firstColon = line.IndexOf(':');
                    int lastColon = line.LastIndexOf(':');
                    if (firstColon >= 0 && firstColon == lastColon)
                    {
                        host = line.Substring(0, firstColon).Trim();
                        if (!int.TryParse(line.Substring(firstColon + 1), out port))
                        {
                            throw new FormatException("Некорректный порт в резервном адресе: " + line);
                        }
                    }
                    else
                    {
                        host = line;
                    }
                }

                host = host.Trim();
                if (host.Length == 0 || port < 1 || port > 65535)
                {
                    throw new FormatException("Некорректный резервный адрес: " + line);
                }
                if (result.Any(e => e.Port == port && string.Equals(e.Host, host, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                if (result.Count >= 7)
                {
                    throw new FormatException("Можно указать не более семи резервных адресов.");
                }

                SftpEndpoint? match = old.FirstOrDefault(e => e.Port == port && string.Equals(e.Host, host, StringComparison.OrdinalIgnoreCase));
                result.Add(new SftpEndpoint
                {
                    Host = host,
                    Port = port,
                    HostKeyFingerprint = match?.HostKeyFingerprint
                });
            }

            return result;
        }

        private void BrowseRemoteFolder()
        {
            _remoteBrowseSucceeded = false;
            try
            {
                Destination edited = GetEditedDestination();
                SftpCredentialLookup credentials = ResolveCredentialsForAction();
                string username = credentials.Username ?? _username.Text.Trim();
                string password = credentials.Password ?? string.Empty;

                using (SftpDirectoryBrowserSession session = SftpTransferService.OpenDirectoryBrowser(
                    edited,
                    username,
                    password,
                    ConfirmUnknownHostKey))
                using (var browser = new SftpRemoteFolderBrowserForm(session, edited.SftpRemotePath ?? string.Empty))
                {
                    if (browser.ShowDialog(this) == DialogResult.OK)
                    {
                        _remotePath.Text = browser.SelectedPath;
                        edited.SftpRemotePath = browser.SelectedPath;
                        _editedDestination.SftpHostKeyFingerprint = edited.SftpHostKeyFingerprint;
                        _editedDestination.SftpFallbacks = edited.SftpFallbacks;
                        _remoteBrowseSucceeded = true;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось получить список удалённых папок:\n" + ex.Message,
                    "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        internal bool BrowseRemoteFolderFromManager()
        {
            BrowseRemoteFolder();
            if (!_remoteBrowseSucceeded)
            {
                return false;
            }

            _destination.SftpRemotePath = _remotePath.Text.Trim();
            _destination.SftpHostKeyFingerprint = _editedDestination.SftpHostKeyFingerprint;
            _destination.SftpFallbacks = _editedDestination.SftpFallbacks;
            return true;
        }

        internal void RunConnectionTestFromManager()
        {
            TestConnection();
            _destination.SftpHostKeyFingerprint = _editedDestination.SftpHostKeyFingerprint;
            _destination.SftpFallbacks = _editedDestination.SftpFallbacks;
        }

        private void TestConnection()
        {
            try
            {
                Destination edited = GetEditedDestination();
                SftpCredentialLookup credentials = ResolveCredentialsForAction();
                string username = credentials.Username ?? _username.Text.Trim();
                string password = credentials.Password ?? string.Empty;

                SftpConnectionTestReport report = SftpTransferService.TestConnectionDetailed(
                    edited, username, password, ConfirmUnknownHostKey);

                if (report.DirectoryMissing && report.ConnectionSucceeded &&
                    MessageBox.Show(this,
                        "Сервер доступен, но удалённая папка не существует:\r\n" + edited.SftpRemotePath +
                        "\r\n\r\nСоздать эту папку (и отсутствующие родительские папки)?",
                        "SFTP — папка не найдена", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    try
                    {
                        SftpTransferService.CreateRemoteDirectory(edited, username, password, ConfirmUnknownHostKey);
                        report = SftpTransferService.TestConnectionDetailed(edited, username, password, ConfirmUnknownHostKey);
                    }
                    catch (Exception createError)
                    {
                        report.DirectoryMessage += "\r\nНе удалось создать папку: " + createError.Message;
                    }
                }

                _editedDestination.SftpHostKeyFingerprint = edited.SftpHostKeyFingerprint;
                _editedDestination.SftpFallbacks = edited.SftpFallbacks;
                bool passed = report.ConnectionSucceeded && report.DirectoryExists &&
                              report.DirectoryReadable && report.DirectoryWritable;
                _hostKeyStatus.Text = report.ConnectionSucceeded
                    ? "Сервер проверен; см. подробный отчёт."
                    : "Проверка соединения не пройдена.";
                _hostKeyStatus.ForeColor = passed ? Color.FromArgb(0, 110, 0) : Color.DarkRed;
                MessageBox.Show(this, FormatConnectionTestReport(report), "Результат проверки SFTP",
                    MessageBoxButtons.OK, passed ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось проверить SFTP-подключение:\n" + ex.Message, "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private SftpCredentialLookup ResolveCredentialsForAction()
        {
            string username = _username.Text.Trim();
            if (_password.Text.Length > 0)
                return SftpCredentialLookup.Available(username, _password.Text);
            if (_credentials.State == SftpCredentialState.Available)
                return _credentials;
            return SftpCredentialStore.GetForUse(_editedDestination, _masterPasswordHint, this);
        }

        private static string FormatConnectionTestReport(SftpConnectionTestReport report)
        {
            return "Подключение и авторизация: " + StatusMark(report.ConnectionSucceeded, report.ConnectionMessage) + " " + report.ConnectionMessage + "\r\n" +
                   "Удалённая папка: " + StatusMark(report.DirectoryExists, report.DirectoryMessage) + " " + report.DirectoryMessage + "\r\n" +
                   "Чтение: " + StatusMark(report.DirectoryReadable, report.ReadMessage) + " " + report.ReadMessage + "\r\n" +
                   "Запись: " + StatusMark(report.DirectoryWritable, report.WriteMessage) + " " + report.WriteMessage;
        }

        private static string StatusMark(bool passed, string message)
        {
            if (passed)
            {
                return "OK";
            }
            return (message ?? string.Empty).IndexOf("не проверено", StringComparison.OrdinalIgnoreCase) >= 0
                ? "—"
                : "Не пройдено";
        }

        private bool ConfirmUnknownHostKey(SftpEndpoint endpoint, string fingerprint)
        {
            DialogResult result = MessageBox.Show(
                this,
                "У этого адреса ещё не сохранён ключ. Сверьте fingerprint с администратором сервера.\n\n" +
                "Сервер: " + endpoint.Host + ":" + endpoint.Port + "\n" +
                "SHA-256: " + fingerprint + "\n\nДоверять этому ключу и сохранить его только для этого адреса?",
                "Проверка SSH-ключа сервера",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            return result == DialogResult.Yes;
        }

        private void SaveAndClose()
        {
            Destination edited;
            try
            {
                edited = GetEditedDestination();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Проверьте параметры SFTP:\n" + ex.Message, "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(edited.Name) || string.IsNullOrWhiteSpace(edited.SftpHost) ||
                string.IsNullOrWhiteSpace(edited.SftpRemotePath) || string.IsNullOrWhiteSpace(_username.Text))
            {
                MessageBox.Show(this, "Укажите название, основной сервер, удалённую папку и имя пользователя.", "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool usernameChanged = !string.Equals(_username.Text.Trim(), _editedDestination.SftpUsername ?? _credentials.Username, StringComparison.Ordinal);
            bool portableExists = !string.IsNullOrWhiteSpace(edited.SftpPasswordEncrypted);
            bool needsPortableWrite = _password.Text.Length > 0 || usernameChanged ||
                                      (!portableExists && _credentials.State == SftpCredentialState.Available);
            if (needsPortableWrite)
            {
                string password = _password.Text;
                if (password.Length == 0 && !usernameChanged && _credentials.State == SftpCredentialState.Available)
                    password = _credentials.Password ?? string.Empty;
                if (password.Length == 0)
                {
                    MessageBox.Show(this, "Введите пароль для выбранного имени пользователя.", "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string? masterPassword = _masterPasswordOverride ?? SftpCredentialStore.GetLocalMasterPassword();
                if (masterPassword == null)
                {
                    if (_hasExistingPortableCredentials)
                    {
                        MessageBox.Show(this,
                            "В конфиге уже есть переносимые записи. Сначала разблокируйте их или измените мастер-пароль на вкладке «Прочее», чтобы не создать записи с разными паролями.",
                            "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    masterPassword = MasterPasswordEntryForm.Prompt(this, null,
                        "Введите новый мастер-пароль. Для управления им и подсказкой откройте вкладку «Прочее».");
                }
                if (masterPassword == null) return;
                try
                {
                    SftpCredentialStore.SavePortable(edited, _username.Text.Trim(), password, masterPassword);
                    MasterPasswordUsed = masterPassword;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Не удалось зашифровать или сохранить пароль:\n" + ex.Message, "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else if (!portableExists && _credentials.State != SftpCredentialState.Available)
            {
                MessageBox.Show(this, "Введите пароль для текущего имени пользователя.", "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            else if (!usernameChanged)
            {
                edited.SftpUsername = _username.Text.Trim();
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
            _destination.SftpFallbacks = edited.SftpFallbacks;
            _destination.SftpChmodExtensions = edited.SftpChmodExtensions;
            _destination.SftpChmodMode = edited.SftpChmodMode;
            _destination.SftpUsername = edited.SftpUsername;
            _destination.SftpPasswordEncrypted = edited.SftpPasswordEncrypted;

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
