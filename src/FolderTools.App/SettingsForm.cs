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
                    if (SftpCredentialStore.Get(destination.Id).State != SftpCredentialState.Available)
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
                    missing + " SFTP-подключений требуют пароля для текущей учётной записи Windows. " +
                    "После переноса программы на новый компьютер или смены учётной записи введите пароли заново.",
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
            _browseFolder.Click += (s, e) => BrowseFolder();
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
                RowCount = 3,
                AutoSize = true,
                Height = 130
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

            var note = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 96,
                Padding = new Padding(0, 12, 0, 0),
                ForeColor = Color.FromArgb(80, 80, 80),
                Text = "Все настройки хранятся в файле " + SettingsStore.FileName
                     + " рядом с FolderTools.exe, иконки — в подпапке "
                     + AppPaths.IconsFolderName + ".\r\n"
                     + "Пути внутри папки программы записываются относительными, поэтому "
                     + "папку можно целиком скопировать или перенести.\r\n"
                     + "Поле DeskEdit можно оставить пустым: тогда программа ищет "
                     + "DeskEdit.exe рядом с собой и в C:\\Tools\\DeskEdit."
            };

            page.Controls.Add(note);
            page.Controls.Add(layout);
            return page;
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
            _browseFolder.Enabled = !isSftp;
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

            using (var form = new SftpDestinationForm(destination))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
            }

            _working.Destinations.Add(destination);
            LoadDestinations();
            _destinations.SelectedIndex = _working.Destinations.Count - 1;
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

            using (var form = new SftpDestinationForm(destination))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
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

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
