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
        if (args.Length >= 1 && args.Length <= 2 && (args[0].StartsWith("--open-window=") || args[0].StartsWith("--reveal-window=")))
        {
            File.AppendAllText(Environment.GetEnvironmentVariable("TESSOA_TEST_CAPTURE"), string.Join("|", args) + "\n");
            // Plays a running tessoa that predates scoped requests: it drops them (exit code 1).
            if (args.Length == 2 && Environment.GetEnvironmentVariable("TESSOA_TEST_REJECT_SCOPE") == "1")
                return 1;
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
            Func<string, bool, string, bool> launchIn = (path, select, scope) => ((Task<bool>)open.Invoke(launcher, new object[] { path, select, scope })).GetAwaiter().GetResult();
            Func<string, bool, bool> launch = (path, select) => launchIn(path, select, null);
            Func<string> captured = () => { string t = File.ReadAllText(capture); File.Delete(capture); return t; };
            string self = Assembly.GetExecutingAssembly().Location;
            Check(FileVersionInfo.GetVersionInfo(self).Comments == "tessoa-file-app-api=1", "the capability marker must be readable from the PE resource");
            remember.Invoke(launcher, new object[] { self });
            string folder = Path.Combine(root, "中文 目录 & %PATH%");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "报告 $value.txt");
            File.WriteAllText(file, "test");
            Check(launch(folder, false), "opening a folder failed");
            Check(captured() == "--open-window=" + folder + "\n", "wrong argument for opening a folder");
            Check(launch(file, true), "revealing a file failed");
            Check(captured() == "--reveal-window=" + file + "\n", "wrong argument for revealing a file");
            Check(launchIn(file, true, "--in-tab=123:45"), "revealing in a tab failed");
            Check(captured() == "--reveal-window=" + file + "|--in-tab=123:45\n", "the scope must travel as its own argument");
            Environment.SetEnvironmentVariable("TESSOA_TEST_REJECT_SCOPE", "1");
            Check(launchIn(folder, false, "--in-window=123"), "a scope the running tessoa drops must be retried without it");
            Check(captured() == "--open-window=" + folder + "|--in-window=123\n--open-window=" + folder + "\n",
                  "the retry must repeat the request without the scope, once");
            Environment.SetEnvironmentVariable("TESSOA_TEST_REJECT_SCOPE", null);
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
            CheckTabParsing();
            Console.WriteLine("PASS: argument quoting, real child-process argv, open/reveal, scoped open and retry, invalid paths, capability check, tab listing parser.");
            return 0;
        }
        finally
        {
            Environment.SetEnvironmentVariable("TESSOA_TEST_CAPTURE", null);
            Environment.SetEnvironmentVariable("TESSOA_TEST_EXIT", null);
            Environment.SetEnvironmentVariable("TESSOA_TEST_REJECT_SCOPE", null);
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// The query-tabs answer: an empty frame means an older tessoa, an api line is always
    /// present otherwise, the path is the rest of the line and may contain spaces.
    /// </summary>
    private static void CheckTabParsing()
    {
        var query = typeof(TessoaPlugin).Assembly.GetType("Listary.FileAppPlugin.Tessoa.TessoaQuery", true);
        var parse = query.GetMethod("ParseTabs", BindingFlags.Static | BindingFlags.NonPublic);
        Func<string, object> run = text => parse.Invoke(null, new object[] { text });
        Func<object, string, object> get = (o, name) => o.GetType().GetProperty(name).GetValue(o);
        Check(run("") == null, "an empty frame means the running tessoa cannot list tabs");
        Check(run("tab 1 c C:\\a\n") == null, "an answer without an api line is not understood");
        object none = run("api 2\n");
        Check(none != null && (int)get(none, "Api") == 2 && ((System.Collections.ICollection)get(none, "Tabs")).Count == 0,
              "no tabs is still a valid answer");
        object list = run("api 2\ntab 17 c D:\\中文 空格\nfuture line\ntab x b C:\\bad\ntab 23 b C:\\dl\n");
        var tabs = (System.Collections.IList)get(list, "Tabs");
        Check(tabs.Count == 2, "malformed and unknown lines must be skipped");
        Check((ulong)get(tabs[0], "Uid") == 17UL && (bool)get(tabs[0], "IsCurrent") && (string)get(tabs[0], "Folder") == "D:\\中文 空格", "first tab");
        Check((ulong)get(tabs[1], "Uid") == 23UL && !(bool)get(tabs[1], "IsCurrent") && (string)get(tabs[1], "Folder") == "C:\\dl", "second tab");
    }

    private static void Check(bool success, string message)
    {
        if (!success) throw new Exception(message);
    }
}
