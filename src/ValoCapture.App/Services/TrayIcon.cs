using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ValoCapture.Core.Game;

namespace ValoCapture.App.Services;

/// <summary>タスクトレイのアイコンとメニュー。</summary>
public sealed class TrayIcon : IDisposable
{
    // メニューに出す録画対象の候補
    private static readonly string[] SelectableQueues =
    [
        QueueIds.Competitive, QueueIds.Unrated, QueueIds.Swiftplay, QueueIds.SpikeRush,
        QueueIds.Deathmatch, QueueIds.TeamDeathmatch, QueueIds.Premier,
    ];

    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _statusItem;
    // 待機中はアプリアイコン、録画中は状態がひと目で分かる赤丸
    private readonly Icon _idleIcon = LoadAppIcon();
    private readonly Icon _recordingIcon = CreateDotIcon(Color.FromArgb(0xff, 0x46, 0x55));

    /// <param name="isQueueSelected">録画対象に含まれているか。</param>
    /// <param name="toggleQueue">録画対象の切り替え（キュー ID, 含めるか）。</param>
    public TrayIcon(Action openViewer, Action openFolder, Action openSettings, Action exit,
        Func<string, bool> isQueueSelected, Action<string, bool> toggleQueue)
    {
        _statusItem = new ToolStripMenuItem("起動中…") { Enabled = false };

        var queues = new ToolStripMenuItem("録画するモード");
        foreach (var q in SelectableQueues)
        {
            var item = new ToolStripMenuItem(DisplayNames.Queue(q)) { CheckOnClick = true };
            item.Click += (_, _) => toggleQueue(q, item.Checked);
            queues.DropDownItems.Add(item);
        }
        // 開くたびに現在の設定を反映する
        queues.DropDownOpening += (_, _) =>
        {
            for (var i = 0; i < SelectableQueues.Length; i++)
                ((ToolStripMenuItem)queues.DropDownItems[i]).Checked = isQueueSelected(SelectableQueues[i]);
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("録画を見る", null, (_, _) => openViewer());
        menu.Items.Add("録画フォルダを開く", null, (_, _) => openFolder());
        menu.Items.Add(queues);
        menu.Items.Add("設定…", null, (_, _) => openSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = _idleIcon,
            Text = "ValoCapture",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) openViewer(); };
    }

    public void Update(string status, bool recording)
    {
        _statusItem.Text = status;
        // NotifyIcon.Text は 63 文字まで
        var text = $"ValoCapture - {status}";
        _icon.Text = text.Length > 63 ? text[..63] : text;
        _icon.Icon = recording ? _recordingIcon : _idleIcon;
    }

    public void Notify(string title, string message) =>
        _icon.ShowBalloonTip(3000, title, message, ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _idleIcon.Dispose();
        _recordingIcon.Dispose();
    }

    private static Icon LoadAppIcon()
    {
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    private static Icon CreateDotIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var ring = new Pen(Color.White, 3);
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, 4, 4, 24, 24);
            g.DrawEllipse(ring, 4, 4, 24, 24);
        }
        var handle = bmp.GetHicon();
        // FromHandle は所有権を持たないので複製して元のハンドルを解放する
        using var temp = Icon.FromHandle(handle);
        var icon = (Icon)temp.Clone();
        DestroyIcon(handle);
        return icon;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
