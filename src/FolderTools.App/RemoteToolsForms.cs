using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FolderTools.App
{
    internal sealed class RemoteProviderCatalogForm : Form
    {
        private readonly TextBox _filter = new TextBox();
        private readonly ListBox _providers = new ListBox();
        private readonly List<RemoteProviderInfo> _allProviders;

        public RemoteProviderCatalogForm(string engineName, IEnumerable<RemoteProviderInfo> providers)
        {
            _allProviders = (providers ?? Enumerable.Empty<RemoteProviderInfo>()).ToList();
            Text = engineName + " — поддерживаемые backend-ы (" + _allProviders.Count + ")";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            ClientSize = new Size(700, 500);
            Font = new Font("Segoe UI", 9F);

            var label = new Label { Text = "Фильтр backend-ов:", Dock = DockStyle.Top, Height = 25 };
            _filter.Dock = DockStyle.Top;
            _filter.TextChanged += (s, e) => Reload();
            _providers.Dock = DockStyle.Fill;
            _providers.IntegralHeight = false;
            _providers.HorizontalScrollbar = true;
            Reload();

            var footer = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                Text = "Список получен напрямую из выбранного исполняемого файла. Поддержка конкретных API может зависеть от версии backend-а.",
                ForeColor = Color.FromArgb(80, 80, 80),
                Padding = new Padding(0, 7, 0, 0)
            };
            Controls.Add(_providers);
            Controls.Add(footer);
            Controls.Add(_filter);
            Controls.Add(label);
        }

        private void Reload()
        {
            string filter = (_filter.Text ?? string.Empty).Trim();
            _providers.BeginUpdate();
            _providers.Items.Clear();
            foreach (RemoteProviderInfo provider in _allProviders.Where(item =>
                         filter.Length == 0 || item.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         item.Description.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                _providers.Items.Add(provider);
            }
            _providers.EndUpdate();
        }
    }

    internal sealed class RemoteConfigPasswordForm : Form
    {
        private readonly TextBox _password = new TextBox();

        public RemoteConfigPasswordForm(string engineName)
        {
            Text = "Пароль конфигурации " + engineName;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(430, 135);
            Font = new Font("Segoe UI", 9F);

            var note = new Label
            {
                Dock = DockStyle.Top,
                Height = 48,
                Text = "Если файл конфигурации rclone зашифрован, введите его пароль. " +
                       "Пустое поле подходит для незашифрованной конфигурации.",
                Padding = new Padding(0, 0, 0, 5)
            };
            _password.Dock = DockStyle.Top;
            _password.UseSystemPasswordChar = true;
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 38,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            var ok = new Button { Text = "Продолжить", Width = 100, Height = 28, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Отмена", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            Controls.Add(_password);
            Controls.Add(buttons);
            Controls.Add(note);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public string Password => _password.Text;
    }
}
