using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Listary.FileAppPlugin.Tessoa
{
    /// <summary>
    /// The write half of the integration: opening a folder, and revealing a file inside it.
    ///
    /// <para>
    ///   Everything goes through tessoa's command line. tessoa already owns single-instance
    ///   forwarding and the foreground handover, so this class only has to name the right
    ///   executable and quote one argument. The current-path pipe stays read-only, and the
    ///   plugin never synthesises keystrokes or borrows the clipboard.
    /// </para>
    /// </summary>
    internal sealed class TessoaLauncher
    {
        // Mirrors the PE "Comments" resource set by tessoa's build.rs. Reading the resource
        // is side-effect free, which is the whole point: probing by launching an unknown build
        // with the new flag would make an older one ignore it and pop up a window instead.
        // Keep this string in sync with build.rs — the two copies have no guard between them.
        internal const string Capability = "tessoa-file-app-api=1";
        private readonly ILogger _logger;
        private string _boundExecutable;

        internal TessoaLauncher(ILogger logger) { _logger = logger; }

        internal void RememberExecutable(string path) { _boundExecutable = path; }

        /// <param name="path">The folder to open, or the item to reveal.</param>
        /// <param name="select">Reveal <paramref name="path"/> in its parent folder.</param>
        /// <param name="scope">
        /// Where to land, as one tessoa argument: <c>--in-window=&lt;hwnd&gt;</c> or
        /// <c>--in-tab=&lt;hwnd&gt;[:&lt;tab id&gt;]</c>. <c>null</c> lets tessoa pick, the
        /// application-level behaviour. Pass a scope only after the running process has reported
        /// file application API 2 (see <see cref="TessoaQuery.GetTabs"/>).
        /// </param>
        internal Task<bool> Open(string path, bool select, string scope = null) => Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                    return false;
                string fullPath = Path.GetFullPath(path);
                if (select ? !File.Exists(fullPath) && !Directory.Exists(fullPath) : !Directory.Exists(fullPath))
                    return false;
                if (select && string.IsNullOrEmpty(Path.GetDirectoryName(fullPath)))
                    return false;

                string executable = FindExecutable();
                if (executable == null)
                {
                    _logger?.LogWarning(
                        "[tessoa] Could not locate tessoa.exe. For a portable install, put its full path in tessoa-path.txt next to this plugin.");
                    return false;
                }
                if (FileVersionInfo.GetVersionInfo(executable).Comments != Capability)
                {
                    _logger?.LogWarning(
                        "[tessoa] This build does not support the open/reveal interfaces; install one with the file application integration: {Executable}",
                        executable);
                    return false;
                }

                string action = QuoteArgument((select ? "--reveal-window=" : "--open-window=") + fullPath);
                int code = Launch(executable, scope == null ? action : action + " " + QuoteArgument(scope));
                // Exit code 1 means the running instance did not take the request. With a scope
                // that happens when the running instance predates it and drops the new request
                // whole, so ask again without one rather than doing nothing.
                if (code == 1 && scope != null)
                {
                    _logger?.LogWarning("[tessoa] The running tessoa did not take {Scope}; opening without it", scope);
                    code = Launch(executable, action);
                }
                if (code != 0)
                    _logger?.LogWarning("[tessoa] The open request failed with exit code {ExitCode}", code);
                return code == 0;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException
                                      || e is ArgumentException || e is System.ComponentModel.Win32Exception
                                      || e is System.Security.SecurityException || e is NotSupportedException)
            {
                _logger?.LogWarning(e, "[tessoa] Could not open the target path {Path}", path);
                return false;
            }
        });

        /// <returns>The child's exit code, or 0 when it is still running.</returns>
        private static int Launch(string executable, string arguments)
        {
            var start = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var process = Process.Start(start))
            {
                if (process == null) return -1;
                // A forwarding child exits within milliseconds, so a non-zero code means the
                // running instance did not take the request and we must not report success.
                // A cold-start process keeps running instead; do not wait out its GUI life.
                return process.WaitForExit(3500) ? process.ExitCode : 0;
            }
        }

        private string FindExecutable()
        {
            // An explicit path wins. If the file exists but names something unusable we fail
            // here rather than silently starting some other installation.
            string directory = PluginLog.AssemblyDirectory();
            string config = directory == null ? null : Path.Combine(directory, "tessoa-path.txt");
            if (config != null && File.Exists(config))
            {
                string path = File.ReadAllText(config).Trim().Trim('"');
                return Path.IsPathRooted(path) && File.Exists(path) ? path : null;
            }
            foreach (string candidate in ExecutableCandidates())
                if (!string.IsNullOrEmpty(candidate) && Path.IsPathRooted(candidate) && File.Exists(candidate))
                    return candidate;
            return null;
        }

        private IEnumerable<string> ExecutableCandidates()
        {
            yield return _boundExecutable;
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using (var root = RegistryKey.OpenBaseKey(hive, view))
                    using (var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\tessoa.exe"))
                        yield return key?.GetValue(null) as string;
                }
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tessoa", "tessoa.exe");
        }

        /// <summary>
        /// Quote one argument by Windows argv rules: backslashes double before a quote and at
        /// the end of the string. Drive roots such as <c>C:\</c> must survive verbatim.
        /// </summary>
        internal static string QuoteArgument(string argument)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c);
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            return result.Append('"').ToString();
        }
    }
}
