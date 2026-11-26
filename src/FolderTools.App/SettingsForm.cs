using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FolderTools.Core;

namespace FolderTools.App
{
    /// <summary>
    /// Settings window: the destination list, their extension patterns and categories.
    ///
    /// Edits happen on a copy of the settings; the registry is rewritten only when the
    /// user saves, so closing the window with Cancel cannot leave the menu and the
    /// settings file disagreeing.
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private readonly FolderToolsSettings _original;
        private FolderToolsSettings _working;
        private readonly ListBox _destinations = new ListBox();
        private readonly ListBox _remoteConnections = new ListBox();
        private readonly ComboBox _remoteEngine = new ComboBox();
        private readonly Label _remoteEngineStatus = new Label();
        private readonly ListBox _patterns = new ListBox();
        private readonly ComboBox _categories = new ComboBox();
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _path = new TextBox();
        private readonly TextBox _patternInput = new TextBox();
        private readonly TextBox _iconPath = new TextBox();
        private readonly PictureBox _iconPreview = new PictureBox();
        private readonly TextBox _deskEditPath = new TextBox();
        private readonly Button _browseFolder = new Button();
        private readonly CheckBox _showSuccess = new CheckBox();
        private string? _pendingMasterPassword;
        private readonly HashSet<string> _stagedCredentialIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Label _hint = new Label();
        private bool _loading;

        public SettingsForm()
        {
            _original = ContextMenuInstaller.LoadSettings();
            _working = _original.Clone();

            Text = SettingsWindowCoordinator.WindowTitle;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(780, 560);
            Size = new Size(860, 620);
            Font = new Font("Segoe UI", 9F);

            // Without this WinForms falls back to its own generic icon, which is what
            // showed in the title bar and on the taskbar instead of the tool's icon.
            Icon = LoadAppIcon();

            BuildLayout();
            LoadDestinations();
            LoadMisc();
            NotifyMissingSftpCredentials();
            FormClosed += (sender, args) =>
            {
                if (DialogResult == DialogResult.OK) return;
                foreach (string id in _stagedCredentialIds)
                {
                    try { SftpCredentialStore.ForgetLocalCredential(id); }
                    catch (Exception) { }
                }
                _stagedCredentialIds.Clear();
            };
        }

        private void NotifyMissingSftpCredentials()
        {
            Destination[] sftpDestinations = _working.Destinations
                .Where(destination => destination.Type == DestinationType.Sftp)
                .ToArray();
            if (sftpDestinations.Length == 0)
            {
                return;
            }

            int missing = 0;
            foreach (Destination destination in sftpDestinations)
            {
                try
                {
                    bool hasPortableSecret = !string.IsNullOrWhiteSpace(destination.SftpPasswordEncrypted);
                    if (!hasPortableSecret && SftpCredentialStore.GetCached(destination).State != SftpCredentialState.Available)
                    {
                        missing++;
                    }
                }
                catch (Exception)
                {
                    missing++;
                }
            }

            if (missing > 0)
            {
                MessageBox.Show(
                    this,
                    missing + " старых SFTP-подключений требуют ввода пароля. Новые portable-записи можно разблокировать мастер-паролем на любом компьютере.",
                    "Обновите пароли SFTP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static Icon? LoadAppIcon()
        {
            try
            {
                Icon? icon = Icon.ExtractAssociatedIcon(ProductInfo.ExecutablePath);
                if (icon != null)
                {
                    return icon;
                }
            }
            catch (Exception)
            {
                // A missing or unreadable exe icon must not stop the window opening.
            }

            return null;
        }

        private void BuildLayout()
        {
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildDestinationsPage());
            tabs.TabPages.Add(BuildRemotePage());
            tabs.TabPages.Add(BuildCategoriesPage());
            tabs.TabPages.Add(BuildMiscPage());
            tabs.TabPages.Add(BuildAboutPage());

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 46,
                Padding = new Padding(8)
            };

            var save = new Button { Text = "Сохранить", DialogResult = DialogResult.None, Width = 110, Height = 30 };
            save.Click += (s, e) => SaveAndClose();
            var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 110, Height = 30 };
            var version = new Label
            {
                Text = "Версия " + ProductInfo.Version,
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 90, 90),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(8, 7, 0, 0)
            };
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(version);

            Controls.Add(tabs);
            Controls.Add(buttons);
            CancelButton = cancel;
        }

        private TabPage BuildDestinationsPage()
        {
            var page = new TabPage("Папки назначения") { Padding = new Padding(10) };

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 300,
                FixedPanel = FixedPanel.Panel1
            };

            _destinations.Dock = DockStyle.Fill;
            _destinations.IntegralHeight = false;
            _destinations.SelectedIndexChanged += (s, e) => ShowSelectedDestination();
            _destinations.DoubleClick += (s, e) =>
            {
                if (Selected?.Type == DestinationType.Sftp)
                {
                    EditSftpDestination();
                }
            };

            // AutoSize on the panel lets it grow when the four buttons do not fit on one
            // row; a fixed height clipped the wrapped second row and hid its labels.
            var listButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(0, 2, 0, 2)
            };
            listButtons.Controls.Add(MakeButton("Добавить", AddDestination));
            listButtons.Controls.Add(MakeButton("Добавить SFTP", AddSftpDestination));
            listButtons.Controls.Add(MakeButton("Изменить SFTP…", EditSftpDestination));
            listButtons.Controls.Add(MakeButton("Удалить", RemoveDestination));
            listButtons.Controls.Add(MakeButton("Вверх", () => MoveDestination(-1)));
            listButtons.Controls.Add(MakeButton("Вниз", () => MoveDestination(1)));

            split.Panel1.Controls.Add(_destinations);
            split.Panel1.Controls.Add(listButtons);

            var details = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 10,
                Padding = new Padding(8, 0, 0, 0)
            };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            details.Controls.Add(new Label { Text = "Название:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            _name.Dock = DockStyle.Fill;
            _name.TextChanged += (s, e) => UpdateSelected(d => d.Name = _name.Text);
            details.Controls.Add(_name, 1, 0);

            details.Controls.Add(new Label { Text = "Папка:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Height = 30 };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            _path.Dock = DockStyle.Fill;
            _path.TextChanged += (s, e) => UpdateSelected(d => d.Path = _path.Text);
            pathRow.Controls.Add(_path, 0, 0);
            _browseFolder.Text = "Обзор…";
            _browseFolder.Width = 86;
            _browseFolder.Height = 28;
            _browseFolder.Click += (s, e) => BrowseSelectedDestination();
            pathRow.Controls.Add(_browseFolder, 1, 0);
            details.Controls.Add(pathRow, 1, 1);

            details.Controls.Add(new Label { Text = "Категория:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            _categories.DropDownStyle = ComboBoxStyle.DropDownList;
            _categories.Dock = DockStyle.Fill;
            _categories.SelectedIndexChanged += (s, e) => UpdateSelected(d => d.Category = SelectedCategoryId());
            details.Controls.Add(_categories, 1, 2);

            details.Controls.Add(new Label { Text = "Иконка:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            var iconRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Height = 32 };
            iconRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            iconRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            iconRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            iconRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

            _iconPreview.Size = new Size(24, 24);
            _iconPreview.Anchor = AnchorStyles.Left;
            _iconPreview.SizeMode = PictureBoxSizeMode.Zoom;
            _iconPreview.BackColor = SystemColors.Control;
            _iconPreview.BorderStyle = BorderStyle.FixedSingle;
            _iconPreview.Margin = new Padding(0);
            _iconPreview.Click += (s, e) => BrowseIcon();

            _iconPath.Dock = DockStyle.Fill;
            _iconPath.ReadOnly = true;
            _iconPath.BackColor = SystemColors.Window;

            iconRow.Controls.Add(_iconPreview, 0, 0);
            iconRow.Controls.Add(_iconPath, 1, 0);
            iconRow.Controls.Add(MakeButton("Обзор…", BrowseIcon), 2, 0);
            details.Controls.Add(iconRow, 1, 3);

            details.Controls.Add(new Label { Text = "Расширения:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
            _patterns.Dock = DockStyle.Fill;
            _patterns.IntegralHeight = false;
            details.Controls.Add(_patterns, 1, 4);

            var patternRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Height = 30 };
            patternRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            patternRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            patternRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            _patternInput.Dock = DockStyle.Fill;
            _patternInput.TextChanged += (s, e) => UpdateHint();
            _patternInput.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    AddPattern();
                }
            };
            patternRow.Controls.Add(_patternInput, 0, 0);
            patternRow.Controls.Add(MakeButton("Добавить", AddPattern), 1, 0);
            patternRow.Controls.Add(MakeButton("Удалить", RemovePattern), 2, 0);
            details.Controls.Add(patternRow, 1, 5);

            _hint.AutoSize = false;
            _hint.Dock = DockStyle.Fill;
            _hint.ForeColor = Color.FromArgb(80, 80, 80);
            details.Controls.Add(_hint, 1, 6);

            split.Panel2.Controls.Add(details);
            page.Controls.Add(split);
            return page;
        }

        private TabPage BuildRemotePage()
        {
            var page = new TabPage("Remote") { Padding = new Padding(10) };
            _remoteConnections.Dock = DockStyle.Fill;
            _remoteConnections.IntegralHeight = false;
            _remoteConnections.DoubleClick += (s, e) => EditRemoteConnection();

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(0, 5, 0, 4)
            };
            buttons.Controls.Add(MakeButton("Добавить SFTP…", AddSftpDestination));
            buttons.Controls.Add(MakeButton("Изменить…", EditRemoteConnection));
            buttons.Controls.Add(MakeButton("Test connection", TestRemoteConnection));
            buttons.Controls.Add(MakeButton("Удалить", RemoveRemoteConnection));
            buttons.Controls.Add(new Label { Text = "Движок:", AutoSize = true, Margin = new Padding(4, 7, 2, 0) });
            _remoteEngine.DropDownStyle = ComboBoxStyle.DropDownList;
            _remoteEngine.Width = 100;
            _remoteEngine.Items.Add("rclone");
            _remoteEngine.Items.Add("bclone");
            _remoteEngine.SelectedIndex = string.Equals(_working.RemoteEngine, "bclone", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            _remoteEngine.SelectedIndexChanged += (s, e) => ChangeRemoteEngine();
            buttons.Controls.Add(_remoteEngine);
            buttons.Controls.Add(MakeButton("Установить / обновить", InstallRemoteEngine));
            buttons.Controls.Add(MakeButton("Backend-ы…", ShowSupportedRemoteProviders));
            buttons.Controls.Add(MakeButton("Проверить настроенные", TestConfiguredRemoteBackends));
            _remoteEngineStatus.AutoSize = true;
            _remoteEngineStatus.Margin = new Padding(3, 7, 0, 0);
            buttons.Controls.Add(_remoteEngineStatus);

            var note = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 82,
                Text = "Здесь собраны отдельные SFTP-подключения получателей. Настройки пароля " +
                       "и удалённой папки открываются двойным щелчком или кнопкой «Изменить».\r\n" +
                       "Для облачных служб выбранный движок: " + _working.RemoteEngine + ". Список backend-ов и тест настроенных подключений берутся из выбранного бинарника; конфиги раздельные и лежат рядом с программой. bclone — неофициальный форк; уникальные backend-ы могут быть недоступны после перехода на rclone.",
                ForeColor = Color.FromArgb(70, 70, 70)
            };

            page.Controls.Add(_remoteConnections);
            page.Controls.Add(buttons);
            page.Controls.Add(note);
            RefreshRemoteEngineStatus();
            return page;
        }

        private RemoteEngine SelectedRemoteEngine => _remoteEngine.SelectedIndex == 1 ? RemoteEngine.Bclone : RemoteEngine.Rclone;

        private void ChangeRemoteEngine()
        {
            string previous = _working.RemoteEngine;
            string current = SelectedRemoteEngine == RemoteEngine.Bclone ? "bclone" : "rclone";
            _working.RemoteEngine = current;
            RefreshRemoteEngineStatus();
            if (string.Equals(previous, "bclone", StringComparison.OrdinalIgnoreCase) && current == "rclone")
            {
                MessageBox.Show(this,
                    "Переход на rclone не удалит bclone и его конфиг, но rclone не знает о дополнительных backend-ах форка (например, Terabox и некоторых других). Такие подключения могут не работать до переключения обратно на bclone или ручного переноса в поддерживаемый backend.",
                    "FolderTools — смена движка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (current == "bclone" && !string.Equals(previous, "bclone", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this,
                    "bclone — неофициальный форк rclone. Поддержка его дополнительных backend-ов зависит от сопровождающего проекта. Конфиг bclone хранится отдельно от конфига rclone.",
                    "FolderTools — bclone", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void RefreshRemoteEngineStatus()
        {
            string engine = SelectedRemoteEngine == RemoteEngine.Rclone ? "rclone" : "bclone";
            try
            {
                _remoteEngineStatus.Text = engine + ": " + RcloneBackendManager.GetVersion(SelectedRemoteEngine);
            }
            catch (Exception ex)
            {
                _remoteEngineStatus.Text = engine + ": " + ex.Message;
            }
        }

        private void InstallRemoteEngine()
        {
            RemoteEngine engine = SelectedRemoteEngine;
            string name = engine == RemoteEngine.Rclone ? "rclone" : "bclone";
            try
            {
                UseWaitCursor = true;
                RcloneBackendManager.InstallOrUpdate(engine);
                RefreshRemoteEngineStatus();
                MessageBox.Show(this, name + " установлен/обновлён. Версия: " + RcloneBackendManager.GetVersion(engine),
                    "FolderTools — Remote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось установить или обновить " + name + ":\r\n" + ex.Message,
                    "FolderTools — Remote", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RefreshRemoteEngineStatus();
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private string? AskRemoteConfigPassword()
        {
            string engineName = SelectedRemoteEngine == RemoteEngine.Rclone ? "rclone" : "bclone";
            using (var form = new RemoteConfigPasswordForm(engineName))
            {
                return form.ShowDialog(this) == DialogResult.OK ? form.Password : null;
            }
        }

        private void ShowSupportedRemoteProviders()
        {
            string? password = AskRemoteConfigPassword();
            if (password == null) return;
            try
            {
                IReadOnlyList<RemoteProviderInfo> providers = RcloneBackendManager.GetProviders(SelectedRemoteEngine, password);
                string engineName = SelectedRemoteEngine == RemoteEngine.Rclone ? "rclone" : "bclone";
                using (var form = new RemoteProviderCatalogForm(engineName, providers))
                {
                    form.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось получить список backend-ов:\r\n" + ex.Message,
                    "FolderTools — Remote", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void TestConfiguredRemoteBackends()
        {
            string? password = AskRemoteConfigPassword();
            if (password == null) return;
            try
            {
                UseWaitCursor = true;
                IReadOnlyList<RemoteTestResult> results = RcloneBackendManager.TestConfiguredRemotes(SelectedRemoteEngine, password);
                string message = string.Join("\r\n\r\n", results.Select(result =>
                    (result.Success ? "OK" : "Ошибка") + " — " + result.Remote + ":\r\n" + result.Message));
                bool allSucceeded = results.All(result => result.Success);
                MessageBox.Show(this, message, "Проверка " + (SelectedRemoteEngine == RemoteEngine.Rclone ? "rclone" : "bclone"),
                    MessageBoxButtons.OK, allSucceeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось проверить настроенные подключения:\r\n" + ex.Message,
                    "FolderTools — Remote", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private TabPage BuildCategoriesPage()
        {
            var page = new TabPage("Категории") { Padding = new Padding(10) };

            var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38 };
            var input = new TextBox { Dock = DockStyle.Bottom, Height = 26 };

            Action reload = () =>
            {
                list.Items.Clear();
                foreach (DestinationCategory category in _working.Categories)
                {
                    list.Items.Add(category.Name);
                }
            };

            buttons.Controls.Add(MakeButton("Добавить", () =>
            {
                string name = (input.Text ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    return;
                }

                if (_working.Categories.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                _working.Categories.Add(new DestinationCategory(Guid.NewGuid().ToString("N"), name));
                input.Clear();
                reload();
                LoadDestinations();
            }));

            buttons.Controls.Add(MakeButton("Удалить", () =>
            {
                int index = list.SelectedIndex;
                if (index < 0)
                {
                    return;
                }

                DestinationCategory category = _working.Categories[index];
                foreach (Destination destination in _working.Destinations
                    .Where(d => string.Equals(d.Category, category.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    destination.Category = null;
                }

                _working.Categories.RemoveAt(index);
                reload();
                LoadDestinations();
            }));

            reload();

            var note = new Label
            {
                Dock = DockStyle.Top,
                Height = 46,
                Text = "Категория собирает несколько папок в одно подменю, чтобы не загромождать "
                     + "основное меню. Удаление категории не удаляет папки: они возвращаются в корень."
            };

            page.Controls.Add(list);
            page.Controls.Add(input);
            page.Controls.Add(buttons);
            page.Controls.Add(note);
            return page;
        }

        private TabPage BuildMiscPage()
        {
            var page = new TabPage("Прочее") { Padding = new Padding(12) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 4,
                AutoSize = true,
                Height = 190
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            layout.Controls.Add(new Label { Text = "DeskEdit (desktop.ini):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            var editorRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Height = 30 };
            editorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            editorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            _deskEditPath.Dock = DockStyle.Fill;
            _deskEditPath.ReadOnly = true;
            _deskEditPath.BackColor = SystemColors.Window;
            editorRow.Controls.Add(_deskEditPath, 0, 0);
            editorRow.Controls.Add(MakeButton("Обзор…", BrowseDeskEdit), 1, 0);
            layout.Controls.Add(editorRow, 1, 0);

            _showSuccess.Text = "Показывать подтверждение после копирования или перемещения";
            _showSuccess.AutoSize = true;
            _showSuccess.Anchor = AnchorStyles.Left;
            layout.Controls.Add(new Label { Text = "Уведомления:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            layout.Controls.Add(_showSuccess, 1, 1);

            layout.Controls.Add(new Label { Text = "Меню Explorer:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            var menuButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                Padding = new Padding(0, 2, 0, 2)
            };
            menuButtons.Controls.Add(MakeButton("Установить / обновить", InstallOrUpdateMenu));
            menuButtons.Controls.Add(MakeButton("Очистить старые пункты", RepairMenuEntries));
            menuButtons.Controls.Add(MakeButton("Удалить меню", UninstallMenu));
            layout.Controls.Add(menuButtons, 1, 2);

            layout.Controls.Add(new Label { Text = "Мастер-пароль SFTP:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            var credentialButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                Padding = new Padding(0, 2, 0, 2)
            };
            credentialButtons.Controls.Add(MakeButton("Задать / изменить пароль и подсказку", ManageMasterPassword));
            credentialButtons.Controls.Add(MakeButton("Забыть на этом компьютере", ForgetLocalCredentials));
            layout.Controls.Add(credentialButtons, 1, 3);

            var note = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 190,
                Padding = new Padding(0, 12, 0, 0),
                ForeColor = Color.FromArgb(80, 80, 80),
                Text = "Все настройки хранятся в файле " + SettingsStore.FileName
                     + " рядом с FolderTools.exe, иконки — в подпапке "
                     + AppPaths.IconsFolderName + ".\r\n"
                     + "Пути внутри папки программы записываются относительными, поэтому "
                     + "папку можно целиком скопировать или перенести.\r\n"
                     + "Установка и удаление меню доступны выше; очистка старых пунктов заново "
                     + "регистрирует FolderTools и убирает отдельные «Копировать в» / «Переместить в».\r\n"
                     + "Поле DeskEdit можно оставить пустым: тогда программа ищет "
                     + "DeskEdit.exe рядом с собой и в C:\\Tools\\DeskEdit.\r\n"
                     + "Зашифрованные пароли лежат в переносимом конфиге; расшифрованные данные и master password — только в Credential Manager этой учётной записи Windows. «Забыть» удаляет локальный кэш только здесь; общего списка устройств нет.\r\n"
                     + "Синхронизируйте FolderTools.settings.json своим клиентом и не редактируйте настройки одновременно на нескольких компьютерах. Программа не объединяет конфликты синхронизации. Смена мастер-пароля не отзывает уже скопированные конфиги или локальные кэши: на других устройствах их нужно забыть отдельно."
            };

            page.Controls.Add(layout);
            page.Controls.Add(note);
            return page;
        }

        private void InstallOrUpdateMenu()
        {
            try
            {
                string[] undeletedDlls = ContextMenuInstaller.CleanupLegacyRootDlls();
                ContextMenuInstaller.Install();
                string message = "Пункты FolderTools установлены/обновлены. Старые отдельные пункты копирования и перемещения очищены.";
                if (undeletedDlls.Length > 0)
                {
                    message += "\r\n\r\nНе удалось удалить некоторые старые DLL из корня папки программы (возможно, они используются):\r\n" +
                               string.Join("\r\n", undeletedDlls);
                }
                MessageBox.Show(this, message,
                    "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось установить пункты меню:\r\n" + ex.Message,
                    "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RepairMenuEntries()
        {
            try
            {
                ContextMenuInstaller.RefreshMenu();
                MessageBox.Show(this, "Меню обновлено. Старые отдельные пункты «Копировать в» и «Переместить в» удалены.",
                    "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось очистить старые пункты меню:\r\n" + ex.Message,
                    "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UninstallMenu()
        {
            if (MessageBox.Show(this,
                    "Удалить пункты FolderTools из контекстного меню Explorer? Настройки и сохранённые пароли останутся.",
                    "Удаление FolderTools", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                ContextMenuInstaller.Uninstall();
                MessageBox.Show(this, "Пункты FolderTools удалены из контекстного меню.",
                    "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось удалить пункты меню:\r\n" + ex.Message,
                    "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BrowseDeskEdit()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Выберите DeskEdit.exe";
                dialog.Filter = "Программы (*.exe)|*.exe|Все файлы (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _deskEditPath.Text = dialog.FileName;
                }
            }
        }

        private TabPage BuildAboutPage()
        {
            var page = new TabPage("Справка") { Padding = new Padding(12) };
            var text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Window,
                Text = HelpText()
            };

            page.Controls.Add(text);
            return page;
        }

        private static string HelpText()
        {
            return
                "Как работает меню\r\n" +
                "\r\n" +
                "Каждая папка из списка даёт пункт «Копировать в» и «Переместить в». " +
                "Пункт появляется только для файлов с подходящим расширением.\r\n" +
                "\r\n" +
                "Шаблоны расширений\r\n" +
                "  *      любые символы в любом количестве\r\n" +
                "  !      ровно один любой символ\r\n" +
                "  *.txt  все файлы .txt\r\n" +
                "  фото!.jpg  файлы вида фото1.jpg, фотоA.jpg\r\n" +
                "  [папка]    только папки\r\n" +
                "\r\n" +
                "Шаблон можно писать без точки: «txt» совпадёт с «.txt». Регистр не важен.\r\n" +
                "\r\n" +
                "Если выделено несколько файлов разных типов, каждый файл уходит в свою папку: " +
                "расширение проверяется отдельно для каждого файла.\r\n" +
                "\r\n" +
                "Одинаковые имена не перезаписываются: к имени добавляется « (2)», « (3)» и так далее.\r\n";
        }

        private static Button MakeButton(string text, Action onClick)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 28, Margin = new Padding(0, 0, 6, 0) };
            button.Click += (s, e) => onClick();
            return button;
        }

        private void LoadMisc()
        {
            _deskEditPath.Text = AppConfig.DeskEditPath ?? string.Empty;
            _showSuccess.Checked = AppConfig.ShowSuccess;
        }

        private void ManageMasterPassword()
        {
            Destination[] sftp = _working.Destinations.Where(d => d.Type == DestinationType.Sftp).ToArray();
            bool hasEncrypted = sftp.Any(d => !string.IsNullOrWhiteSpace(d.SftpPasswordEncrypted));
            using (var form = new MasterPasswordSetupForm(_working.MasterPasswordHint, hasEncrypted))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                if (form.NewPassword.Length < 12)
                {
                    MessageBox.Show(this, "Используйте не менее 12 символов для мастер-пароля.", "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!string.Equals(form.NewPassword, form.Confirmation, StringComparison.Ordinal))
                {
                    MessageBox.Show(this, "Повторный ввод мастер-пароля не совпадает.", "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var prepared = new List<Tuple<Destination, string, string>>();
                try
                {
                    foreach (Destination destination in sftp)
                    {
                        string? username = destination.SftpUsername;
                        string? password = null;
                        if (!string.IsNullOrWhiteSpace(destination.SftpPasswordEncrypted))
                        {
                            password = PortableCredentialCrypto.Decrypt(destination.SftpPasswordEncrypted!, form.CurrentPassword);
                        }
                        else
                        {
                            SftpCredentialLookup cached = SftpCredentialStore.GetCached(destination);
                            if (cached.State == SftpCredentialState.Available)
                            {
                                username = cached.Username;
                                password = cached.Password;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(username) && password != null)
                            prepared.Add(Tuple.Create(destination, username!, password));
                    }
                }
                catch (System.Security.Cryptography.CryptographicException ex)
                {
                    MessageBox.Show(this, "Не удалось проверить текущий мастер-пароль. Ничего не изменено.\r\n" + ex.Message,
                        "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var encrypted = new List<Tuple<Destination, string, string>>();
                foreach (Tuple<Destination, string, string> item in prepared)
                {
                    encrypted.Add(Tuple.Create(item.Item1, item.Item2,
                        PortableCredentialCrypto.Encrypt(item.Item3, form.NewPassword)));
                }
                foreach (Tuple<Destination, string, string> item in encrypted)
                {
                    item.Item1.SftpUsername = item.Item2;
                    item.Item1.SftpPasswordEncrypted = item.Item3;
                }

                _working.MasterPasswordHint = form.Hint;
                _pendingMasterPassword = form.NewPassword;
                MessageBox.Show(this,
                    "Мастер-пароль и зашифрованные записи подготовлены. Они будут сохранены вместе с настройками после нажатия «Сохранить».",
                    "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ForgetLocalCredentials()
        {
            if (MessageBox.Show(this,
                    "Удалить локальные кэши SFTP и мастер-пароль только из Credential Manager этой учётной записи Windows? " +
                    "Зашифрованный конфиг не изменится. Кэши на других компьютерах эта операция не затронет.",
                    "Забыть пароли на этом компьютере", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            try
            {
                SftpCredentialStore.ForgetLocal(_working.Destinations);
                _pendingMasterPassword = null;
                MessageBox.Show(this, "Локальный кэш удалён. При следующем использовании SFTP потребуется разблокировать конфиг.",
                    "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось забыть локальные учётные данные:\r\n" + ex.Message,
                    "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadDestinations()
        {
            _loading = true;
            int selected = _destinations.SelectedIndex;
            _destinations.Items.Clear();
            foreach (Destination destination in _working.Destinations)
            {
                string? categoryName = CategoryName(destination.Category);
                string label = destination.DisplayName;
                if (!string.IsNullOrEmpty(categoryName))
                {
                    label = "[" + categoryName + "] " + label;
                }

                if (!destination.IsEnabled)
                {
                    label += "  (нет расширений)";
                }

                _destinations.Items.Add(label);
            }

            if (_destinations.Items.Count > 0)
            {
                _destinations.SelectedIndex = Math.Min(Math.Max(selected, 0), _destinations.Items.Count - 1);
            }

            _loading = false;

            // The selection change above fires while _loading is set, so the detail
            // fields are filled here instead.
            ShowSelectedDestination();
            LoadRemoteConnections();
        }

        private void LoadRemoteConnections()
        {
            int selected = _remoteConnections.SelectedIndex;
            _remoteConnections.Items.Clear();
            foreach (Destination destination in _working.Destinations.Where(d => d.Type == DestinationType.Sftp))
            {
                string host = destination.SftpHost ?? string.Empty;
                string endpoint = host + (destination.SftpPort == 22 ? string.Empty : ":" + destination.SftpPort);
                _remoteConnections.Items.Add(destination.DisplayName + " — " + endpoint + (destination.SftpRemotePath ?? string.Empty));
            }

            if (_remoteConnections.Items.Count > 0)
            {
                _remoteConnections.SelectedIndex = Math.Min(Math.Max(selected, 0), _remoteConnections.Items.Count - 1);
            }
        }

        private Destination? SelectedRemoteConnection()
        {
            Destination[] connections = _working.Destinations
                .Where(d => d.Type == DestinationType.Sftp)
                .ToArray();
            int index = _remoteConnections.SelectedIndex;
            return index >= 0 && index < connections.Length ? connections[index] : null;
        }

        private bool HasPortableSftpCredentials => _working.Destinations.Any(d =>
            d.Type == DestinationType.Sftp && !string.IsNullOrWhiteSpace(d.SftpPasswordEncrypted));

        private void EditRemoteConnection()
        {
            Destination? destination = SelectedRemoteConnection();
            if (destination == null)
            {
                return;
            }

            using (var form = new SftpDestinationForm(destination, _pendingMasterPassword, _working.MasterPasswordHint, HasPortableSftpCredentials))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    _pendingMasterPassword = form.MasterPasswordUsed ?? _pendingMasterPassword;
                    LoadDestinations();
                }
            }
        }

        private void RemoveRemoteConnection()
        {
            Destination? destination = SelectedRemoteConnection();
            if (destination == null)
            {
                return;
            }

            if (MessageBox.Show(this,
                    "Удалить SFTP-получатель «" + destination.DisplayName + "»?",
                    "FolderTools", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _working.Destinations.Remove(destination);
            LoadDestinations();
        }

        private void TestRemoteConnection()
        {
            Destination? destination = SelectedRemoteConnection();
            if (destination == null)
            {
                MessageBox.Show(this, "Сначала выберите SFTP-получателя.", "FolderTools",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var form = new SftpDestinationForm(destination, _pendingMasterPassword, _working.MasterPasswordHint, HasPortableSftpCredentials))
            {
                form.RunConnectionTestFromManager();
            }
            LoadDestinations();
        }

        private string? CategoryName(string? categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return null;
            }

            return _working.Categories
                .FirstOrDefault(c => string.Equals(c.Id, categoryId, StringComparison.OrdinalIgnoreCase))?.Name;
        }

        private Destination? Selected =>
            _destinations.SelectedIndex >= 0 && _destinations.SelectedIndex < _working.Destinations.Count
                ? _working.Destinations[_destinations.SelectedIndex]
                : null;

        private void LoadCategoryChoices()
        {
            _loading = true;
            _categories.Items.Clear();
            _categories.Items.Add("(без категории)");
            foreach (DestinationCategory category in _working.Categories)
            {
                _categories.Items.Add(category.Name);
            }

            Destination? destination = Selected;
            int index = 0;
            if (destination != null && !string.IsNullOrWhiteSpace(destination.Category))
            {
                for (int i = 0; i < _working.Categories.Count; i++)
                {
                    if (string.Equals(_working.Categories[i].Id, destination.Category, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i + 1;
                        break;
                    }
                }
            }

            if (_categories.Items.Count > 0)
            {
                _categories.SelectedIndex = index;
            }

            _loading = false;
        }

        private string? SelectedCategoryId()
        {
            int index = _categories.SelectedIndex;
            if (index <= 0 || index - 1 >= _working.Categories.Count)
            {
                return null;
            }

            return _working.Categories[index - 1].Id;
        }

        private void ShowSelectedDestination()
        {
            if (_loading)
            {
                return;
            }

            Destination? destination = Selected;
            _loading = true;
            if (destination == null)
            {
                _name.Text = string.Empty;
                _path.Text = string.Empty;
                _path.ReadOnly = false;
                _browseFolder.Enabled = true;
                ShowDestinationIcon(null);
                _patterns.Items.Clear();
                _loading = false;
                LoadCategoryChoices();
                UpdateHint();
                return;
            }

            _name.Text = destination.Name;
            bool isSftp = destination.Type == DestinationType.Sftp;
            _path.ReadOnly = isSftp;
            _browseFolder.Enabled = true;
            _path.Text = isSftp
                ? "sftp://" + (destination.SftpHost ?? string.Empty) + ":" + destination.SftpPort + (destination.SftpRemotePath ?? string.Empty)
                : destination.Path;
            ShowDestinationIcon(destination.IconPath);
            _patterns.Items.Clear();
            foreach (string pattern in destination.Patterns)
            {
                _patterns.Items.Add(pattern);
            }

            _loading = false;
            LoadCategoryChoices();
            UpdateHint();
        }

        private void UpdateSelected(Action<Destination> update)
        {
            if (_loading)
            {
                return;
            }

            Destination? destination = Selected;
            if (destination == null)
            {
                return;
            }

            update(destination);

            // Keep the list label in step with the edit without losing the selection.
            int index = _destinations.SelectedIndex;
            _loading = true;
            string? categoryName = CategoryName(destination.Category);
            string label = destination.DisplayName;
            if (!string.IsNullOrEmpty(categoryName))
            {
                label = "[" + categoryName + "] " + label;
            }

            if (!destination.IsEnabled)
            {
                label += "  (нет расширений)";
            }

            if (index >= 0)
            {
                _destinations.Items[index] = label;
            }

            _loading = false;
        }

        private void UpdateHint()
        {
            string text = _patternInput.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                _hint.Text = "Например: *.txt, *.jpg, фото!.png, [папка]";
                _hint.ForeColor = Color.FromArgb(80, 80, 80);
                return;
            }

            if (ExtensionPattern.IsFolderPattern(text))
            {
                _hint.Text = "Только папки.";
                _hint.ForeColor = Color.FromArgb(0, 110, 0);
                return;
            }

            if (ExtensionPattern.IsMatchAll(text))
            {
                _hint.Text = "Любой файл, включая файлы без расширения.";
                _hint.ForeColor = Color.FromArgb(150, 100, 0);
                return;
            }

            string sample = text.Replace("*", "abc").Replace("!", "1");
            _hint.Text = "Совпадёт с файлом «файл" + ExtensionPattern.Normalize(sample) + "»";
            _hint.ForeColor = Color.FromArgb(80, 80, 80);
        }

        private void AddDestination()
        {
            var destination = new Destination
            {
                Name = "Новая папка",
                Path = string.Empty
            };

            _working.Destinations.Add(destination);
            LoadDestinations();
            _destinations.SelectedIndex = _working.Destinations.Count - 1;
            _name.Focus();
            _name.SelectAll();
        }

        private void AddSftpDestination()
        {
            var destination = new Destination
            {
                Name = "Новая SFTP-папка",
                Path = string.Empty,
                Type = DestinationType.Sftp,
                SftpPort = 22
            };

            Destination[] savedSftp = _working.Destinations
                .Where(item => item.Type == DestinationType.Sftp)
                .ToArray();
            if (savedSftp.Length > 0)
            {
                using (var picker = new SftpConnectionPickerForm(savedSftp))
                {
                    DialogResult choice = picker.ShowDialog(this);
                    if (choice == DialogResult.Cancel)
                    {
                        return;
                    }

                    if (choice == DialogResult.OK && picker.SelectedConnection != null)
                    {
                        Destination source = picker.SelectedConnection;
                        destination.SftpHost = source.SftpHost;
                        destination.SftpPort = source.SftpPort;
                        destination.SftpFallbacks = (source.SftpFallbacks ?? new List<SftpEndpoint>())
                            .Select(endpoint => endpoint.Clone()).ToList();
                        destination.SftpHostKeyFingerprint = source.SftpHostKeyFingerprint;
                        if (SftpCredentialStore.Copy(source, destination))
                        {
                            _stagedCredentialIds.Add(destination.Id);
                        }
                        else
                        {
                            MessageBox.Show(this,
                                "Параметры сервера скопированы, но у этого профиля нет переносимого пароля. " +
                                "Введите пароль при настройке новой папки.",
                                "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                }
            }

            using (var form = new SftpDestinationForm(destination, _pendingMasterPassword, _working.MasterPasswordHint, HasPortableSftpCredentials))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                _pendingMasterPassword = form.MasterPasswordUsed ?? _pendingMasterPassword;
            }

            _working.Destinations.Add(destination);
            LoadDestinations();
            _destinations.SelectedIndex = _working.Destinations.Count - 1;
            _remoteConnections.SelectedIndex = _remoteConnections.Items.Count - 1;
        }

        private void EditSftpDestination()
        {
            Destination? destination = Selected;
            if (destination == null)
            {
                return;
            }

            if (destination.Type != DestinationType.Sftp)
            {
                MessageBox.Show(this, "Сначала выберите SFTP-папку в списке.", "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var form = new SftpDestinationForm(destination, _pendingMasterPassword, _working.MasterPasswordHint, HasPortableSftpCredentials))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    _pendingMasterPassword = form.MasterPasswordUsed ?? _pendingMasterPassword;
                    LoadDestinations();
                }
            }
        }

        private void RemoveDestination()
        {
            int index = _destinations.SelectedIndex;
            if (index < 0)
            {
                return;
            }

            Destination destination = _working.Destinations[index];
            DialogResult confirm = MessageBox.Show(
                "Удалить папку «" + destination.DisplayName + "» из списка?",
                "FolderTools",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            _working.Destinations.RemoveAt(index);
            LoadDestinations();
        }

        private void MoveDestination(int delta)
        {
            int index = _destinations.SelectedIndex;
            int target = index + delta;
            if (index < 0 || target < 0 || target >= _working.Destinations.Count)
            {
                return;
            }

            Destination item = _working.Destinations[index];
            _working.Destinations.RemoveAt(index);
            _working.Destinations.Insert(target, item);
            LoadDestinations();
            _destinations.SelectedIndex = target;
        }

        /// <summary>
        /// Shows the configured icon, or a blank preview when the entry uses the
        /// tool's own icon.
        /// </summary>
        private void ShowDestinationIcon(string? storedIconPath)
        {
            _iconPreview.Image?.Dispose();
            _iconPreview.Image = null;

            if (string.IsNullOrWhiteSpace(storedIconPath))
            {
                _iconPath.Text = "(иконка программы)";
                return;
            }

            string full = AppPaths.Resolve(BaseDirectory, storedIconPath);
            _iconPath.Text = storedIconPath;

            try
            {
                if (File.Exists(full))
                {
                    using (var icon = new Icon(full, 24, 24))
                    {
                        _iconPreview.Image = icon.ToBitmap();
                    }
                }
            }
            catch (Exception)
            {
                // A broken or unsupported icon file must not break the window; the
                // path stays visible so the user can pick another one.
            }
        }

        private void BrowseIcon()
        {
            Destination? destination = Selected;
            if (destination == null)
            {
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Выберите иконку";
                dialog.Filter = "Иконки ICO и PNG (*.ico;*.png)|*.ico;*.png|ICO (*.ico)|*.ico|PNG (*.png)|*.png|Все файлы (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    destination.IconPath = IconImporter.Import(
                        BaseDirectory,
                        destination.Id,
                        dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Не удалось скопировать иконку:\n" + ex.Message,
                        "FolderTools",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                ShowDestinationIcon(destination.IconPath);
            }
        }

        private static string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;

        private void BrowseSelectedDestination()
        {
            Destination? destination = Selected;
            if (destination == null || destination.Type != DestinationType.Sftp)
            {
                BrowseFolder();
                return;
            }

            using (var form = new SftpDestinationForm(destination, _pendingMasterPassword, _working.MasterPasswordHint, HasPortableSftpCredentials))
            {
                if (form.BrowseRemoteFolderFromManager())
                {
                    LoadDestinations();
                }
            }
        }

        private void BrowseFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку назначения";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _path.Text = dialog.SelectedPath;
                }
            }
        }

        private void AddPattern()
        {
            Destination? destination = Selected;
            if (destination == null)
            {
                return;
            }

            string pattern = (ExtensionPattern.Normalize(_patternInput.Text));
            if (pattern.Length == 0)
            {
                return;
            }

            if (destination.Patterns.Any(p => string.Equals(
                ExtensionPattern.Normalize(p), pattern, StringComparison.OrdinalIgnoreCase)))
            {
                _patternInput.Clear();
                return;
            }

            destination.Patterns.Add(pattern);
            _patterns.Items.Add(pattern);
            _patternInput.Clear();
            UpdateSelected(d => { });
            _patterns.SelectedIndex = _patterns.Items.Count - 1;
        }

        private void RemovePattern()
        {
            Destination? destination = Selected;
            int index = _patterns.SelectedIndex;
            if (destination == null || index < 0 || index >= destination.Patterns.Count)
            {
                return;
            }

            destination.Patterns.RemoveAt(index);
            _patterns.Items.RemoveAt(index);
            UpdateSelected(d => { });
            UpdateHint();
        }

        private void SaveAndClose()
        {
            foreach (Destination destination in _working.Destinations)
            {
                if (string.IsNullOrWhiteSpace(destination.EffectivePath))
                {
                    MessageBox.Show(
                        "У папки «" + destination.DisplayName + "» не указан путь.",
                        "FolderTools",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            // The misc fields must go into _working before the single save below:
            // writing them separately would let this save overwrite them with the
            // values read when the window opened.
            _working.DeskEditPath = string.IsNullOrWhiteSpace(_deskEditPath.Text)
                ? string.Empty
                : AppPaths.ToStored(BaseDirectory, _deskEditPath.Text);
            _working.ShowSuccess = _showSuccess.Checked;
            string? savedHint = _working.MasterPasswordHint;
            _working.MasterPasswordHint = string.IsNullOrWhiteSpace(savedHint) ? null : savedHint!.Trim();

            try
            {
                ContextMenuInstaller.SaveSettings(_working);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось сохранить настройки:\n" + ex.Message,
                    "FolderTools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            string? pendingMasterPassword = _pendingMasterPassword;
            if (!string.IsNullOrWhiteSpace(pendingMasterPassword))
            {
                try
                {
                    SftpCredentialStore.SaveLocalMasterPassword(pendingMasterPassword!);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        "Настройки сохранены, но мастер-пароль не удалось сохранить локально. При следующем использовании его нужно будет ввести снова.\r\n" + ex.Message,
                        "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            string? cacheMasterPassword = pendingMasterPassword ?? SftpCredentialStore.GetLocalMasterPassword();
            if (!string.IsNullOrWhiteSpace(cacheMasterPassword))
            {
                foreach (Destination destination in _working.Destinations.Where(d =>
                             d.Type == DestinationType.Sftp && !string.IsNullOrWhiteSpace(d.SftpPasswordEncrypted)))
                {
                    Destination? previous = _original.Destinations.FirstOrDefault(d =>
                        string.Equals(d.Id, destination.Id, StringComparison.Ordinal));
                    if (previous != null && string.Equals(previous.SftpPasswordEncrypted, destination.SftpPasswordEncrypted, StringComparison.Ordinal))
                        continue;
                    try
                    {
                        SftpCredentialStore.CachePortable(destination, cacheMasterPassword!);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this,
                            "Настройки сохранены, но локальный кэш «" + destination.DisplayName + "» не обновлён. Пароль будет запрошен при первом использовании.\r\n" + ex.Message,
                            "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }

            foreach (Destination removed in _original.Destinations.Where(d => d.Type == DestinationType.Sftp &&
                         !_working.Destinations.Any(current => string.Equals(current.Id, d.Id, StringComparison.Ordinal))))
            {
                try { SftpCredentialStore.ForgetLocalCredential(removed.Id); }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        "Настройки сохранены, но локальный секрет удалённого SFTP-получателя «" + removed.DisplayName + "» не удалось очистить.\r\n" + ex.Message,
                        "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            foreach (string id in _stagedCredentialIds.ToArray())
            {
                if (!_working.Destinations.Any(d => string.Equals(d.Id, id, StringComparison.Ordinal)))
                {
                    try { SftpCredentialStore.ForgetLocalCredential(id); }
                    catch (Exception) { }
                    _stagedCredentialIds.Remove(id);
                }
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
