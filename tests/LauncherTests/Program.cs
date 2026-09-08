using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Listary.FileAppPlugin.Tessoa;

internal static class Program
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static int Main(string[] args)
    {
        // Re-entry as the child: a real process reports back the argv it received, which is
        // the only way to catch a quote or a drive-root backslash being eaten in transit.
        if (args.Length == 1 && (args[0].StartsWith("--open-window=") || args[0].StartsWith("--reveal-window=")))
        {
            File.WriteAllText(Environment.GetEnvironmentVariable("TESSOA_TEST_CAPTURE"), args[0]);
            return int.TryParse(Environment.GetEnvironmentVariable("TESSOA_TEST_EXIT"), out int code) ? code : 0;
        }

        var type = typeof(TessoaPlugin).Assembly.GetType("Listary.FileAppPlugin.Tessoa.TessoaLauncher", true);
        var quote = type.GetMethod("QuoteArgument", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (string input in new[] { "", "C:\\", @"\\server\share\", "中文 空格 & %PATH% $value.txt", "a\tfile", "a\\\"b", "末尾 \\\\" })
        {
            string encoded = (string)quote.Invoke(null, new object[] { input });
            IntPtr argv = CommandLineToArgvW("test.exe " + encoded, out int count);
            try
            {
                Check(argv != IntPtr.Zero && count == 2, "the argument was split into several");
                Check(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, IntPtr.Size)) == input, "Windows argv round-trip changed the value");
            }
            finally { if (argv != IntPtr.Zero) LocalFree(argv); }
        }

        string root = Path.Combine(Path.GetTempPath(), "tessoa-launcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string capture = Path.Combine(root, "captured.txt");
        Environment.SetEnvironmentVariable("TESSOA_TEST_CAPTURE", capture);
        try
        {
            object launcher = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { null }, null);
            var remember = type.GetMethod("RememberExecutable", BindingFlags.Instance | BindingFlags.NonPublic);
            var open = type.GetMethod("Open", BindingFlags.Instance | BindingFlags.NonPublic);
            Func<string, bool, bool> launch = (path, select) => ((Task<bool>)open.Invoke(launcher, new object[] { path, select })).GetAwaiter().GetResult();
            string self = Assembly.GetExecutingAssembly().Location;
            Check(FileVersionInfo.GetVersionInfo(self).Comments == "tessoa-file-app-api=1", "the capability marker must be readable from the PE resource");
            remember.Invoke(launcher, new object[] { self });
            string folder = Path.Combine(root, "中文 目录 & %PATH%");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "报告 $value.txt");
            File.WriteAllText(file, "test");
            Check(launch(folder, false), "opening a folder failed");
            Check(File.ReadAllText(capture) == "--open-window=" + folder, "wrong argument for opening a folder");
            Check(launch(file, true), "revealing a file failed");
            Check(File.ReadAllText(capture) == "--reveal-window=" + file, "wrong argument for revealing a file");
            Check(launch(folder, true), "revealing a folder failed");
            Environment.SetEnvironmentVariable("TESSOA_TEST_EXIT", "7");
            Check(!launch(folder, false), "a child that exits non-zero must not be reported as success");
            Environment.SetEnvironmentVariable("TESSOA_TEST_EXIT", null);
            Check(!launch(file, false), "a file passed as a folder must be rejected");
            Check(!launch(Path.Combine(root, "missing"), true), "a missing path must be rejected");
            Check(!launch("bad\npath", true), "a path containing a newline must be rejected");
            File.Delete(capture);
            remember.Invoke(launcher, new object[] { Path.Combine(Environment.SystemDirectory, "cmd.exe") });
            Check(!launch(folder, false), "a build without the capability marker must not be launched");
            Check(!File.Exists(capture), "nothing may be started once the request is rejected");
            Console.WriteLine("PASS: argument quoting, real child-process argv, open/reveal, invalid paths, capability check.");
            return 0;
        }
        finally
        {
            Environment.SetEnvironmentVariable("TESSOA_TEST_CAPTURE", null);
            Environment.SetEnvironmentVariable("TESSOA_TEST_EXIT", null);
            Directory.Delete(root, true);
        }
    }

    private static void Check(bool success, string message)
    {
        if (!success) throw new Exception(message);
    }
}
