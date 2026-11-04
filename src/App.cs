using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("显卡名称工具")]
[assembly: System.Reflection.AssemblyDescription("修改显示适配器 DeviceDesc，支持原始值备份和恢复")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace GpuNameTool
{
    static class Program
    {
        public static string BackupFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GpuNameTool", "Backups");
        public static bool Admin { get { using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); } }
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--diagnose")
            {
                try
                {
                    var store = new RegistryDeviceStore();
                    var lines = new List<string> { "READ ONLY - no device settings changed", "Admin: " + Admin };
                    foreach (string id in NativeDevices.PresentIds())
                    { var d = store.Read(id); lines.Add("Device: " + d.DisplayName); lines.Add("FriendlyName: " + d.FriendlyName); lines.Add("PNP: " + id); }
                    File.WriteAllLines(args[1], lines, new UTF8Encoding(true)); return 0;
                }
                catch (Exception e) { File.WriteAllText(args[1], e.ToString(), Encoding.UTF8); return 1; }
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\GpuNameTool-" + NameService.Hash(Environment.UserName) + (Admin ? "-Admin" : "-User"), out created))
            {
                if (!created) { MessageBox.Show("工具已经打开，请切换到已有窗口。", "显卡名称工具"); return 0; }
                Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
                try { Application.Run(new MainForm()); return 0; }
                catch (Exception e) { MessageBox.Show(e.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
            }
        }
    }

    sealed class MainForm : Form
    {
        readonly RegistryDeviceStore store = new RegistryDeviceStore();
        readonly NameService service;
        readonly ComboBox devices = new ComboBox(), names = new ComboBox();
        readonly TextBox details = new TextBox();
        readonly Button refresh = new Button(), apply = new Button(), restore = new Button(), elevate = new Button(), backup = new Button();
        readonly Label status = new Label(), backupStatus = new Label();
        bool busy;
        public MainForm()
        {
            service = new NameService(store, Program.BackupFolder);
            Text = "显卡名称工具";
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(780, 650); MinimumSize = new Size(796, 689);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 250);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 11 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            float[] heights = { 42, 44, 30, 42, 142, 30, 42, 38, 52, 46 };
            foreach (float h in heights) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(grid);
            var title = new Label { Text = "显卡名称工具", Font = new Font(Font.FontFamily, 20, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Fill };
            grid.Controls.Add(title, 0, 0); grid.SetColumnSpan(title, 2);
            var intro = new Label { Text = "修改系统中的显卡描述名称，支持备份与恢复。\n名称变化不会改变显卡硬件、显存或性能。", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(75, 85, 99) };
            grid.Controls.Add(intro, 0, 1); grid.SetColumnSpan(intro, 2);
            grid.Controls.Add(new Label { Text = "1  选择显示适配器", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            devices.DropDownStyle = ComboBoxStyle.DropDownList; devices.Dock = DockStyle.Fill; devices.Margin = new Padding(0, 4, 12, 4);
            devices.SelectedIndexChanged += (s, e) => UpdateDevice(); grid.Controls.Add(devices, 0, 3);
            SetupButton(refresh, "刷新列表", (s, e) => LoadDevices()); grid.Controls.Add(refresh, 1, 3);
            details.Multiline = true; details.ReadOnly = true; details.ScrollBars = ScrollBars.Vertical; details.Dock = DockStyle.Fill;
            details.BackColor = Color.White; details.Margin = new Padding(0, 6, 0, 12);
            grid.Controls.Add(details, 0, 4); grid.SetColumnSpan(details, 2);
            grid.Controls.Add(new Label { Text = "2  输入新名称，或选择预设", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 5);
            names.DropDownStyle = ComboBoxStyle.DropDown; names.MaxLength = 200; names.Dock = DockStyle.Fill; names.Margin = new Padding(0, 4, 12, 4);
            names.Items.AddRange(new object[] { "NVIDIA GeForce GTX 1050 Ti", "Radeon RX 560" });
            names.TextChanged += (s, e) => UpdateButtons(); grid.Controls.Add(names, 0, 6);
            SetupButton(apply, "备份并修改", (s, e) => Change()); apply.BackColor = Color.FromArgb(30, 92, 180); apply.ForeColor = Color.White;
            grid.Controls.Add(apply, 1, 6);
            backupStatus.Dock = DockStyle.Fill; backupStatus.AutoEllipsis = true; backupStatus.ForeColor = Color.FromArgb(75, 85, 99);
            grid.Controls.Add(backupStatus, 0, 7); grid.SetColumnSpan(backupStatus, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            SetupButton(backup, "仅备份当前值", (s, e) => Operate(() => service.Backup(store.Read(SelectedId())), "备份已保存。已有备份会保留，不会覆盖。"));
            SetupButton(restore, "恢复原始备份", (s, e) => Restore());
            var folder = new Button(); SetupButton(folder, "打开备份目录", (s, e) =>
            { try { Directory.CreateDirectory(Program.BackupFolder); Process.Start("explorer.exe", "\"" + Program.BackupFolder + "\""); } catch (Exception ex) { ShowError(ex); } });
            actions.Controls.AddRange(new Control[] { backup, restore, folder }); grid.Controls.Add(actions, 0, 8); grid.SetColumnSpan(actions, 2);
            var privilege = new Label { Text = Program.Admin ? "当前权限：管理员" : "当前权限：普通用户 · 可查看和备份，修改与恢复需要管理员权限", Dock = DockStyle.Fill, AutoSize = true, Anchor = AnchorStyles.Left };
            grid.Controls.Add(privilege, 0, 9);
            SetupButton(elevate, "管理员模式", (s, e) => Elevate()); elevate.Visible = !Program.Admin; grid.Controls.Add(elevate, 1, 9);
            status.Dock = DockStyle.Fill; status.ForeColor = Color.FromArgb(75, 85, 99); status.Text = "正在检测显卡…";
            grid.Controls.Add(status, 0, 10); grid.SetColumnSpan(status, 2);
            Shown += (s, e) => LoadDevices();
        }
        static void SetupButton(Button b, string text, EventHandler handler)
        {
            b.Text = text; b.AutoSize = false; b.Size = new Size(140, 36); b.Dock = DockStyle.None;
            b.Margin = new Padding(0, 4, 12, 4); b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Color.FromArgb(204, 211, 220);
            b.UseVisualStyleBackColor = false; b.BackColor = Color.White; b.Click += handler;
        }
        string SelectedId()
        {
            var d = devices.SelectedItem as DeviceSnapshot;
            if (d == null) throw new InvalidOperationException("请先选择显卡。");
            return d.DeviceId;
        }
        void UpdateButtons()
        {
            bool selected = devices.SelectedItem is DeviceSnapshot;
            apply.Enabled = !busy && selected && Program.Admin && !String.IsNullOrWhiteSpace(names.Text);
            restore.Enabled = !busy && selected && Program.Admin && File.Exists(service.BackupPath((DeviceSnapshot)devices.SelectedItem));
            backup.Enabled = !busy && selected; refresh.Enabled = !busy; devices.Enabled = !busy; names.Enabled = !busy;
        }
        async void LoadDevices()
        {
            if (busy) return;
            string previous = devices.SelectedItem is DeviceSnapshot ? SelectedId() : null;
            busy = true; UpdateButtons(); status.Text = "正在检测显卡…";
            try
            {
                var result = await Task.Run(() => NativeDevices.PresentIds().Select(id => store.Read(id)).ToList());
                devices.Items.Clear(); devices.Items.AddRange(result.Cast<object>().ToArray());
                devices.SelectedIndex = result.Count == 0 ? -1 : Math.Max(0, result.FindIndex(d => d.DeviceId == previous));
                status.Text = result.Count == 0 ? "未找到当前连接的显示适配器。请检查设备后刷新。" : "已检测到 " + result.Count + " 个显示适配器。选择设备后可查看详情。";
                UpdateDevice();
            }
            catch (Exception e) { status.Text = "检测失败：" + e.Message; details.Text = "请刷新重试。"; }
            finally { busy = false; UpdateButtons(); }
        }
        void UpdateDevice()
        {
            var d = devices.SelectedItem as DeviceSnapshot;
            if (d == null) { details.Clear(); backupStatus.Text = ""; UpdateButtons(); return; }
            details.Text = "当前描述：" + d.DisplayName + "\r\n驱动报告：" + (d.DriverName ?? "（未提供）")
                + "\r\n设备编号：" + d.DeviceId + "\r\n原始描述内容：" + (d.Value ?? "（不存在）")
                + (!String.IsNullOrEmpty(d.FriendlyName) ? "\r\nFriendlyName：" + d.FriendlyName + "（部分界面会优先显示此名称）" : "");
            backupStatus.Text = File.Exists(service.BackupPath(d)) ? "已有当前设备与驱动的备份 · 修改时保留首次备份" : "尚无备份 · 修改前会自动保存当前原始值";
            UpdateButtons();
        }
        void Change()
        {
            try
            {
                string name = NameService.ValidateName(names.Text), id = SelectedId();
                if (MessageBox.Show(this, "设备：" + ((DeviceSnapshot)devices.SelectedItem).DisplayName + "\n新描述：" + name
                    + "\n\n将先保存备份，再修改设备描述。是否继续？", "确认修改", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                Operate(() => service.Change(id, name), "描述已写入并验证。部分界面需要关闭重开或手动重启后才更新；驱动和游戏可能仍显示真实型号。");
            }
            catch (Exception e) { ShowError(e); }
        }
        void Restore()
        {
            if (MessageBox.Show(this, "将恢复为本工具首次备份时的原始值，备份文件会保留。是否继续？", "确认恢复", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Operate(() => service.Restore(SelectedId()), "原始描述已恢复并验证。部分界面需要关闭重开或手动重启后才更新。");
        }
        void Operate(Action action, string success)
        {
            if (busy) return;
            busy = true; UpdateButtons();
            try
            {
                action();
                int i = devices.SelectedIndex;
                DeviceSnapshot latest = store.Read(((DeviceSnapshot)devices.Items[i]).DeviceId);
                devices.Items[i] = latest; devices.SelectedIndex = i; UpdateDevice();
                status.Text = success;
                MessageBox.Show(this, success, "操作完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (UnauthorizedAccessException) { ShowError(new Exception("系统拒绝写入。请以管理员权限运行；若仍被拒绝，说明该设备项受保护，工具不会强制修改权限。")); }
            catch (Exception e) { ShowError(e); }
            finally { busy = false; UpdateButtons(); }
        }
        void ShowError(Exception e)
        {
            status.Text = "操作未完成：" + e.Message;
            MessageBox.Show(this, e.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        void Elevate()
        {
            try
            {
                // Allow the elevated instance to acquire its own mutex after this process exits.
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas" });
                Close();
            }
            catch (System.ComponentModel.Win32Exception e) { if (e.NativeErrorCode != 1223) ShowError(e); }
        }
    }
}
