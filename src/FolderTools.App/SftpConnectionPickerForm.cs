using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FolderTools.Core;

namespace FolderTools.App
{
    /// <summary>Lets the user reuse an existing SFTP endpoint or configure a new one.</summary>
    internal sealed class SftpConnectionPickerForm : Form
    {
        private readonly List<Destination> _connections;
        private readonly ListBox _list = new ListBox();

        public SftpConnectionPickerForm(IEnumerable<Destination> connections)
        {
            _connections = (connections ?? Enumerable.Empty<Destination>()).ToList();
            Text = "Добавить SFTP-папку";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(510, 270);
            Font = new Font("Segoe UI", 9F);

            var note = new Label
            {
                Dock = DockStyle.Top,
                Height = 46,
                Text = "Можно настроить новое подключение или создать папку назначения на основе " +
                       "сохранённого SFTP-получателя. Адреса и учётные данные будут скопированы; " +
                       "удалённую папку выберете отдельно.",
                Padding = new Padding(0, 0, 0, 6)
            };
            _list.Dock = DockStyle.Fill;
            _list.IntegralHeight = false;
            _list.DisplayMember = nameof(ConnectionChoice.Label);
            foreach (Destination destination in _connections)
            {
                string endpoint = destination.SftpHost ?? string.Empty;
                if (destination.SftpPort != 22)
                {
                    endpoint += ":" + destination.SftpPort;
                }
                _list.Items.Add(new ConnectionChoice(destination,
                    destination.DisplayName + " — " + endpoint + " (" + (destination.SftpRemotePath ?? string.Empty) + ")"));
            }
            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = 0;
            }
            _list.DoubleClick += (s, e) => SelectExisting();

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 5, 0, 0)
            };
            var cancel = new Button { Text = "Отмена", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            var existing = new Button { Text = "Использовать выбранный", Width = 170, Height = 28, Enabled = _connections.Count > 0 };
            existing.Click += (s, e) => SelectExisting();
            var fresh = new Button { Text = "Новое подключение", Width = 145, Height = 28, DialogResult = DialogResult.Retry };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(existing);
            buttons.Controls.Add(fresh);

            Controls.Add(_list);
            Controls.Add(buttons);
            Controls.Add(note);
            CancelButton = cancel;
        }

        public Destination? SelectedConnection { get; private set; }

        private void SelectExisting()
        {
            if (_list.SelectedItem is ConnectionChoice choice)
            {
                SelectedConnection = choice.Destination;
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private sealed class ConnectionChoice
        {
            public ConnectionChoice(Destination destination, string label)
            {
                Destination = destination;
                Label = label;
            }

            public Destination Destination { get; }
            public string Label { get; }
        }
    }
}
