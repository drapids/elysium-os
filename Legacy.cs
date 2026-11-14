using System;
using System.Diagnostics;
using System.IO;

namespace Elysium
{
    internal static class Legacy
    {
        public static void StopOld()
        {
            try
            {
                string me = Environment.ProcessPath ?? "";
                foreach (var p in Process.GetProcessesByName("heft"))
                {
                    try
                    {
                        string? path = p.MainModule?.FileName;
                        if (path != null && path.Equals(me, StringComparison.OrdinalIgnoreCase)) continue;
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch
            {
            }
        }

        public static void Migrate()
        {
            Move(Environment.SpecialFolder.ApplicationData);
            Move(Environment.SpecialFolder.LocalApplicationData);
        }

        static void Move(Environment.SpecialFolder root)
        {
            try
            {
                string baseDir = Environment.GetFolderPath(root);
                string from = Path.Combine(baseDir, "heft");
                string to = Path.Combine(baseDir, "elysium");
                if (!Directory.Exists(from) || File.Exists(Path.Combine(from, ".moved"))) return;
                if (!Directory.Exists(to))
                {
                    try
                    {
                        Directory.Move(from, to);
                        return;
                    }
                    catch
                    {
                    }
                }
                Merge(from, to);
                File.WriteAllText(Path.Combine(from, ".moved"), "everything moved to " + to);
            }
            catch
            {
            }
        }

        static void Merge(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from))
            {
                string dest = Path.Combine(to, Path.GetFileName(file));
                if (File.Exists(dest)) continue;
                try
                {
                    File.Copy(file, dest);
                }
                catch
                {
                }
            }
            foreach (var dir in Directory.EnumerateDirectories(from))
            {
                try
                {
                    Merge(dir, Path.Combine(to, Path.GetFileName(dir)));
                }
                catch
                {
                }
            }
        }
    }
}
