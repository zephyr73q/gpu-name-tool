using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace GpuNameTool
{
    public sealed class DeviceSnapshot
    {
        public string DeviceId, Fingerprint, DriverName, FriendlyName;
        public bool Exists;
        public RegistryValueKind Kind;
        public string Value;
        public string DisplayName { get { return Clean(Value ?? DriverName ?? DeviceId); } }
        public static string Clean(string value)
        {
            if (String.IsNullOrEmpty(value)) return "（未设置）";
            int split = value.LastIndexOf(';');
            return value.StartsWith("@", StringComparison.Ordinal) && split >= 0 ? value.Substring(split + 1) : value;
        }
        public override string ToString() { return DisplayName; }
    }

    public interface IDeviceStore
    {
        DeviceSnapshot Read(string id);
        void Write(DeviceSnapshot expected, bool exists, RegistryValueKind kind, string value);
    }

    public sealed class BackupRecord
    {
        public int Version = 1;
        public string DeviceId, Fingerprint, CreatedUtc, Value;
        public bool Exists;
        public int Kind;
    }

    public sealed class NameService
    {
        readonly IDeviceStore store;
        readonly string folder;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        public NameService(IDeviceStore store, string folder) { this.store = store; this.folder = folder; }
        public static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }
        public string BackupPath(DeviceSnapshot device)
        {
            return Path.Combine(folder, Hash(device.DeviceId.ToUpperInvariant() + "\n" + device.Fingerprint) + ".json");
        }
        public static string ValidateName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 200 || name.Any(Char.IsControl))
                throw new InvalidOperationException("请输入 1–200 个字符的名称，不要包含换行或控制字符。");
            return name;
        }
        static void ValidateBackup(BackupRecord b, DeviceSnapshot device)
        {
            if (b == null || b.Version != 1 || !String.Equals(b.DeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase)
                || b.Fingerprint != device.Fingerprint || (b.Kind != (int)RegistryValueKind.String && b.Kind != (int)RegistryValueKind.ExpandString)
                || (b.Exists && (b.Value == null || b.Value.Length > 32767)))
                throw new InvalidDataException("备份与当前设备不匹配或内容损坏，已停止操作。原备份不会被覆盖。");
        }
        BackupRecord Load(DeviceSnapshot device)
        {
            string path = BackupPath(device);
            if (!File.Exists(path)) throw new FileNotFoundException("当前设备和驱动没有本工具创建的备份，无法恢复。", path);
            if (new FileInfo(path).Length > 262144) throw new InvalidDataException("备份文件过大，已停止操作。");
            BackupRecord b;
            try { b = json.Deserialize<BackupRecord>(File.ReadAllText(path, Encoding.UTF8)); }
            catch (Exception e) { throw new InvalidDataException("无法读取备份，已停止操作。请保留原文件。", e); }
            ValidateBackup(b, device);
            return b;
        }
        public void Backup(DeviceSnapshot device)
        {
            string path = BackupPath(device);
            if (File.Exists(path)) { Load(device); return; }
            var b = new BackupRecord { DeviceId = device.DeviceId, Fingerprint = device.Fingerprint,
                CreatedUtc = DateTime.UtcNow.ToString("o"), Exists = device.Exists, Kind = (int)device.Kind, Value = device.Value };
            ValidateBackup(b, device);
            Directory.CreateDirectory(folder);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(json.Serialize(b));
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                File.Move(temp, path);
                Load(device); // Validate the persisted backup before permitting a device write.
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public void Change(string id, string name)
        {
            name = ValidateName(name);
            DeviceSnapshot before = store.Read(id);
            Backup(before);
            store.Write(before, true, RegistryValueKind.String, name);
        }
        public void Restore(string id)
        {
            DeviceSnapshot before = store.Read(id);
            BackupRecord b = Load(before);
            store.Write(before, b.Exists, (RegistryValueKind)b.Kind, b.Value);
        }
    }

    public sealed class RegistryDeviceStore : IDeviceStore
    {
        public const string DisplayClass = "{4d36e968-e325-11ce-bfc1-08002be10318}";
        const string EnumRoot = @"SYSTEM\CurrentControlSet\Enum\";
        static RegistryKey Machine() { return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); }
        static string ValidId(string id)
        {
            if (String.IsNullOrWhiteSpace(id) || id.Length > 1024 || id.Split('\\').Length != 3
                || id.Split('\\').Any(s => s.Length == 0 || s == "." || s == "..") || id.Any(Char.IsControl) || id.Contains("/"))
                throw new InvalidDataException("设备编号无效。");
            return id;
        }
        static DeviceSnapshot Snapshot(RegistryKey machine, RegistryKey key, string id)
        {
            if (!String.Equals(key.GetValue("ClassGUID") as string, DisplayClass, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("选中项目不是显示适配器，已停止操作。");
            var hardware = key.GetValue("HardwareID") as string[] ?? new string[0];
            string driver = key.GetValue("Driver") as string ?? "";
            string driverName = "", driverIdentity = "", machineId = Environment.MachineName;
            if (driver.StartsWith(DisplayClass + "\\", StringComparison.OrdinalIgnoreCase) && driver.Split('\\').Length == 2)
                using (var d = machine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\" + driver))
                    if (d != null)
                    {
                        driverName = d.GetValue("DriverDesc") as string;
                        driverIdentity = Convert.ToString(d.GetValue("DriverVersion")) + "\n" + Convert.ToString(d.GetValue("InfPath"));
                    }
            using (var crypt = machine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                if (crypt != null) machineId = Convert.ToString(crypt.GetValue("MachineGuid"));
            bool exists = key.GetValueNames().Any(v => String.Equals(v, "DeviceDesc", StringComparison.OrdinalIgnoreCase));
            RegistryValueKind kind = exists ? key.GetValueKind("DeviceDesc") : RegistryValueKind.String;
            if (kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString)
                throw new InvalidDataException("设备描述不是受支持的字符串类型，已停止操作。");
            return new DeviceSnapshot { DeviceId = id, Exists = exists, Kind = kind,
                Value = exists ? (string)key.GetValue("DeviceDesc", null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null,
                FriendlyName = key.GetValue("FriendlyName") as string, DriverName = driverName,
                Fingerprint = NameService.Hash(machineId + "\n" + String.Join("\n", hardware) + "\n" + driver + "\n" + driverIdentity) };
        }
        public DeviceSnapshot Read(string id)
        {
            id = ValidId(id);
            using (var machine = Machine())
            using (var key = machine.OpenSubKey(EnumRoot + id))
            {
                if (key == null) throw new InvalidOperationException("设备已不存在，请刷新列表。");
                return Snapshot(machine, key, id);
            }
        }
        public void Write(DeviceSnapshot expected, bool exists, RegistryValueKind kind, string value)
        {
            string id = ValidId(expected.DeviceId);
            if (kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString) throw new InvalidDataException("值类型无效。");
            if (exists && value == null) throw new InvalidDataException("写入内容不能为空值。");
            if (!NativeDevices.PresentIds().Contains(id, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("设备已断开，请刷新列表。");
            using (var machine = Machine())
            using (var key = machine.OpenSubKey(EnumRoot + id, RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.QueryValues | RegistryRights.SetValue))
            {
                if (key == null) throw new InvalidOperationException("设备已不存在，请刷新列表。");
                DeviceSnapshot now = Snapshot(machine, key, id);
                if (now.Fingerprint != expected.Fingerprint || now.Exists != expected.Exists || now.Kind != expected.Kind || now.Value != expected.Value)
                    throw new InvalidOperationException("设备或描述刚刚发生变化，请刷新后重试。");
                if (exists) key.SetValue("DeviceDesc", value, kind);
                else key.DeleteValue("DeviceDesc", false);
                key.Flush();
                now = Snapshot(machine, key, id);
                if (now.Exists != exists || (exists && (now.Value != value || now.Kind != kind)))
                    throw new IOException("写入后的检查未通过。备份已保留，请刷新后尝试恢复。");
            }
        }
    }

    public static class NativeDevices
    {
        [StructLayout(LayoutKind.Sequential)]
        struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid cls, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo info);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref DeviceInfo info, StringBuilder id, uint size, out uint required);
        [DllImport("setupapi.dll")]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        public static List<string> PresentIds()
        {
            Guid cls = new Guid(RegistryDeviceStore.DisplayClass);
            IntPtr set = SetupDiGetClassDevs(ref cls, null, IntPtr.Zero, 2);
            if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var result = new List<string>();
                for (uint index = 0; ; index++)
                {
                    var info = new DeviceInfo { Size = (uint)Marshal.SizeOf(typeof(DeviceInfo)) };
                    if (!SetupDiEnumDeviceInfo(set, index, ref info))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == 259) break;
                        throw new Win32Exception(error);
                    }
                    var id = new StringBuilder(4096); uint needed;
                    if (!SetupDiGetDeviceInstanceId(set, ref info, id, (uint)id.Capacity, out needed))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    result.Add(id.ToString());
                }
                return result;
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
    }
}
