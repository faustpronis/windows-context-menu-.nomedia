using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FolderTools.App
{
    internal sealed class SftpTransferProgressForm : Form
    {
        private readonly Func<Action<SftpTransferProgress>, CancellationToken, SftpTransferResult> _operation;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Label _status = new Label();
        private readonly Label _file = new Label();
        private readonly Label _details = new Label();
        private readonly Button _cancel = new Button();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastReportedBytes;
        private long _lastReportedMilliseconds;
        private long _lastUiPostMilliseconds;
        private double _bytesPerSecond;
        private bool _finished;

        public SftpTransferProgressForm(Func<Action<SftpTransferProgress>, CancellationToken, SftpTransferResult> operation)
        {
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
            Text = "SFTP — передача файлов";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 190);
            Font = new Font("Segoe UI", 9F);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                ColumnCount = 1,
                RowCount = 5
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

            _status.Dock = DockStyle.Fill;
            _status.Text = "Подготовка передачи…";
            _status.AutoEllipsis = true;
            layout.Controls.Add(_status, 0, 0);
            _file.Dock = DockStyle.Fill;
            _file.AutoEllipsis = true;
            layout.Controls.Add(_file, 0, 1);
            _progress.Dock = DockStyle.Fill;
            _progress.Minimum = 0;
            _progress.Maximum = 1000;
            layout.Controls.Add(_progress, 0, 2);
            _details.Dock = DockStyle.Fill;
            _details.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(_details, 0, 3);
            var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            _cancel.Text = "Отмена";
            _cancel.Width = 90;
            _cancel.Height = 28;
            _cancel.Click += (s, e) => RequestCancel();
            buttonRow.Controls.Add(_cancel);
            layout.Controls.Add(buttonRow, 0, 4);
            Controls.Add(layout);

            FormClosing += (s, e) =>
            {
                if (!_finished)
                {
                    e.Cancel = true;
                    RequestCancel();
                }
            };
        }

        public SftpTransferResult? Result { get; private set; }
        public Exception? Failure { get; private set; }

        public T InvokeOnUi<T>(Func<T> action)
        {
            if (InvokeRequired)
            {
                return (T)Invoke(action);
            }

            return action();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Task.Run(() => _operation(ReportProgress, _cancellation.Token)).ContinueWith(task =>
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                try
                {
                    BeginInvoke(new Action(() => Complete(task)));
                }
                catch (InvalidOperationException)
                {
                    // The dialog is already closing; the worker has completed.
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void ReportProgress(SftpTransferProgress update)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            long now = _clock.ElapsedMilliseconds;
            bool important = !string.Equals(update.Status, "Загрузка файла…", StringComparison.Ordinal);
            if (!important && now - Interlocked.Read(ref _lastUiPostMilliseconds) < 100)
            {
                return;
            }
            Interlocked.Exchange(ref _lastUiPostMilliseconds, now);

            try
            {
                BeginInvoke(new Action(() => UpdateProgress(update)));
            }
            catch (InvalidOperationException)
            {
                // The window has been disposed; the service ignores callback exceptions.
            }
        }

        private void UpdateProgress(SftpTransferProgress update)
        {
            if (IsDisposed)
            {
                return;
            }

            _status.Text = update.Status;
            _file.Text = string.IsNullOrWhiteSpace(update.CurrentFile)
                ? string.Empty
                : "Файл: " + System.IO.Path.GetFileName(update.CurrentFile);

            if (update.BytesTotal > 0)
            {
                _progress.Value = (int)Math.Max(0, Math.Min(1000, update.BytesTransferredTotal * 1000.0 / update.BytesTotal));
            }
            else if (update.TotalItems > 0)
            {
                _progress.Value = (int)Math.Max(0, Math.Min(1000, update.CompletedItems * 1000.0 / update.TotalItems));
            }

            long elapsed = _clock.ElapsedMilliseconds;
            long deltaBytes = update.BytesTransferredTotal - _lastReportedBytes;
            long deltaTime = elapsed - _lastReportedMilliseconds;
            if (deltaBytes > 0 && deltaTime > 0)
            {
                double measured = deltaBytes * 1000.0 / deltaTime;
                _bytesPerSecond = _bytesPerSecond == 0 ? measured : (_bytesPerSecond * 0.65) + (measured * 0.35);
            }
            _lastReportedBytes = update.BytesTransferredTotal;
            _lastReportedMilliseconds = elapsed;

            string totals = FormatBytes(update.BytesTransferredTotal) + " из " + FormatBytes(update.BytesTotal);
            string speed = _bytesPerSecond > 0 ? "  •  " + FormatBytes((long)_bytesPerSecond) + "/с" : string.Empty;
            string item = update.FileBytesTotal > 0
                ? "  •  файл " + FormatBytes(update.FileBytesTransferred) + " из " + FormatBytes(update.FileBytesTotal)
                : string.Empty;
            _details.Text = "Объектов: " + update.CompletedItems + " из " + update.TotalItems + "  •  " + totals + speed + item;
        }

        private void Complete(Task<SftpTransferResult> task)
        {
            if (task.IsCanceled)
            {
                Result = new SftpTransferResult(new string[0], new string[0], true, _lastReportedBytes);
            }
            else if (task.IsFaulted)
            {
                Failure = task.Exception?.GetBaseException() ?? new Exception("Неизвестная ошибка SFTP.");
            }
            else
            {
                Result = task.Result;
            }

            _finished = true;
            _cancel.Enabled = false;
            Close();
        }

        private void RequestCancel()
        {
            if (_finished || _cancellation.IsCancellationRequested)
            {
                return;
            }

            _cancel.Enabled = false;
            _cancel.Text = "Отмена…";
            _status.Text = "Останавливаем передачу и удаляем временный файл…";
            _cancellation.Cancel();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " Б";
            }
            double value = bytes;
            string[] units = { "КБ", "МБ", "ГБ", "ТБ" };
            int unit = -1;
            do
            {
                value /= 1024.0;
                unit++;
            } while (value >= 1024.0 && unit < units.Length - 1);
            return value.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " " + units[unit];
        }
    }
}
