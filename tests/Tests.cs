using System;
using System.IO;
using System.Text;
using Microsoft.Win32;
using GpuNameTool;

sealed class MemoryStore : IDeviceStore
{
    public DeviceSnapshot State = new DeviceSnapshot { DeviceId = @"PCI\TEST\INSTANCE", Fingerprint = "test-machine-device-driver", Exists = true, Kind = RegistryValueKind.ExpandString, Value = @"@%SystemRoot%\test.inf;%name%;原始显卡" };
    public int Writes;
    public bool Deny;
    public DeviceSnapshot Read(string id)
    {
        if (id != State.DeviceId) throw new Exception("unexpected device");
        return new DeviceSnapshot { DeviceId = State.DeviceId, Fingerprint = State.Fingerprint, Exists = State.Exists, Kind = State.Kind, Value = State.Value };
    }
    public void Write(DeviceSnapshot expected, bool exists, RegistryValueKind kind, string value)
    {
        if (Deny) throw new UnauthorizedAccessException();
        Writes++; State.Exists = exists; State.Kind = kind; State.Value = value;
    }
}

static class Tests
{
    static int passed;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); } catch { rejected = true; }
        Check(rejected, "expected rejection");
    }
    static void Test(string name, Action action) { action(); Console.WriteLine("PASS " + name); passed++; }
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        int number = 0;
        Func<string> folder = () => Path.Combine(root, (++number).ToString());
        Test("Unicode change, immutable first backup, exact ExpandString restore", () => {
            var store = new MemoryStore(); string original = store.State.Value;
            var service = new NameService(store, folder()); string path = service.BackupPath(store.State);
            service.Change(store.State.DeviceId, "  自定义显卡 RTX 例子  ");
            Check(store.State.Value == "自定义显卡 RTX 例子", "Unicode or trim failed");
            string firstBackup = File.ReadAllText(path);
            service.Change(store.State.DeviceId, "Second name");
            Check(File.ReadAllText(path) == firstBackup, "first backup overwritten");
            service.Restore(store.State.DeviceId);
            Check(store.State.Value == original && store.State.Kind == RegistryValueKind.ExpandString, "exact restore failed");
        });
        Test("Absent original value is removed on restore", () => {
            var store = new MemoryStore(); store.State.Exists = false; store.State.Value = null; store.State.Kind = RegistryValueKind.String;
            var service = new NameService(store, folder()); service.Change(store.State.DeviceId, "new"); service.Restore(store.State.DeviceId);
            Check(!store.State.Exists, "absent original was not restored");
        });
        Test("Invalid names cause no device writes or backup", () => {
            var store = new MemoryStore(); string f = folder(); var service = new NameService(store, f);
            foreach (var name in new[] { "", "   ", "line\nbreak", "nul\0name", new string('a', 201) }) Reject(() => service.Change(store.State.DeviceId, name));
            Check(store.Writes == 0 && !Directory.Exists(f), "validation was too late");
        });
        Test("Broken backup blocks modification and remains untouched", () => {
            var store = new MemoryStore(); var service = new NameService(store, folder()); service.Backup(store.State);
            string p = service.BackupPath(store.State); File.WriteAllText(p, "broken json");
            Reject(() => service.Change(store.State.DeviceId, "new"));
            Check(store.Writes == 0 && File.ReadAllText(p) == "broken json", "broken backup bypassed");
        });
        Test("Device identity mismatch blocks restore", () => {
            var store = new MemoryStore(); var service = new NameService(store, folder()); service.Backup(store.State);
            string p = service.BackupPath(store.State); string data = File.ReadAllText(p);
            File.WriteAllText(p, data.Replace("test-machine-device-driver", "another-device"));
            Reject(() => service.Restore(store.State.DeviceId)); Check(store.Writes == 0, "wrong-device restore");
        });
        Test("Cannot save backup means no device write", () => {
            var store = new MemoryStore(); string f = folder(); File.WriteAllText(f, "file blocks folder");
            var service = new NameService(store, f); Reject(() => service.Change(store.State.DeviceId, "new"));
            Check(store.Writes == 0, "write happened without backup");
        });
        Test("Denied device write preserves valid backup and original state", () => {
            var store = new MemoryStore(); store.Deny = true; string original = store.State.Value;
            var service = new NameService(store, folder()); Reject(() => service.Change(store.State.DeviceId, "new"));
            Check(store.State.Value == original && File.Exists(service.BackupPath(store.State)), "denial handling failed");
        });
        Test("No backup means no restore write", () => {
            var store = new MemoryStore(); var service = new NameService(store, folder()); Reject(() => service.Restore(store.State.DeviceId));
            Check(store.Writes == 0, "restore without backup");
        });
        Test("Driver identity creates separate backup", () => {
            var store = new MemoryStore(); var service = new NameService(store, folder()); service.Backup(store.State);
            store.State.Fingerprint = "new-driver"; Reject(() => service.Restore(store.State.DeviceId));
            Check(store.Writes == 0, "old driver backup restored");
        });
        Test("Malformed registry paths rejected before opening keys", () => {
            var store = new RegistryDeviceStore();
            foreach (var id in new[] { "", @"..\..\SOFTWARE", @"PCI\TEST\INSTANCE\EXTRA", "PCI\\TEST\\bad\0id" }) Reject(() => store.Read(id));
        });
        Console.WriteLine(passed + " tests passed. No physical device settings were modified."); return 0;
    }
}
