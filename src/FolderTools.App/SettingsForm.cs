using System;
using System.Collections.Generic;
using System.Drawing;
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
        private readonly Label _hint = new Label();
        private readonly CheckBox _categoryMode = new CheckBox();
        private bool _loading;

        public SettingsForm()
        {
            _original = ContextMenuInstaller.LoadSettings();
            _working = _original.Clone();

            Text = "FolderTools — настройки";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(780, 560);
            Size = new Size(860, 620);
            Font = new Font("Segoe UI", 9F);

            BuildLayout();
            LoadDestinations();
        }

        private void BuildLayout()
        {
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildDestinationsPage());
            tabs.TabPages.Add(BuildCategoriesPage());
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
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);

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

            var listButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38 };
            listButtons.Controls.Add(MakeButton("Добавить", AddDestination));
            listButtons.Controls.Add(MakeButton("Удалить", RemoveDestination));
            listButtons.Controls.Add(MakeButton("Вверх", () => MoveDestination(-1)));
            listButtons.Controls.Add(MakeButton("Вниз", () => MoveDestination(1)));

            split.Panel1.Controls.Add(_destinations);
            split.Panel1.Controls.Add(listButtons);

            var details = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 8,
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
            pathRow.Controls.Add(MakeButton("Обзор…", BrowseFolder), 1, 0);
            details.Controls.Add(pathRow, 1, 1);

            details.Controls.Add(new Label { Text = "Категория:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            _categories.DropDownStyle = ComboBoxStyle.DropDownList;
            _categories.Dock = DockStyle.Fill;
            _categories.SelectedIndexChanged += (s, e) => UpdateSelected(d => d.Category = SelectedCategoryId());
            details.Controls.Add(_categories, 1, 2);

            details.Controls.Add(new Label { Text = "Расширения:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            _patterns.Dock = DockStyle.Fill;
            _patterns.IntegralHeight = false;
            details.Controls.Add(_patterns, 1, 3);

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
            details.Controls.Add(patternRow, 1, 4);

            _hint.AutoSize = false;
            _hint.Dock = DockStyle.Fill;
            _hint.ForeColor = Color.FromArgb(80, 80, 80);
            details.Controls.Add(_hint, 1, 5);

            _categoryMode.Text = "Показывать этот пункт в подменю категории";
            _categoryMode.AutoSize = true;
            _categoryMode.Enabled = false;
            _categoryMode.Visible = false;
            details.Controls.Add(_categoryMode, 1, 6);

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
                LoadCategoryChoices();
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
                LoadCategoryChoices();
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
            else
            {
                ShowSelectedDestination();
            }

            _loading = false;
            LoadCategoryChoices();
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
                _patterns.Items.Clear();
                _loading = false;
                UpdateHint();
                return;
            }

            _name.Text = destination.Name;
            _path.Text = destination.Path;
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
                if (string.IsNullOrWhiteSpace(destination.Path))
                {
                    MessageBox.Show(
                        "У папки «" + destination.DisplayName + "» не указан путь.",
                        "FolderTools",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

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
