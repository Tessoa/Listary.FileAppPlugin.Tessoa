using System;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Listary.FileAppPlugin.Tessoa
{
    /// <summary>
    /// Logging with a diagnostic switch.
    ///
    /// <para>
    ///   Listary's own log keeps warnings and above, so anything this plugin writes at Debug level
    ///   is invisible in the field. That is the wrong trade-off while something is being diagnosed:
    ///   "Listary never asked us" and "we answered nothing" look identical from outside, and both
    ///   look like "the plugin does not work".
    /// </para>
    /// <para>
    ///   Dropping a file named <c>verbose.txt</c> next to the plugin assembly promotes these
    ///   messages to Warning, so they land in Listary's log without a special build. Remove the
    ///   file to go quiet again. Either way, restart Listary to reload the plugin.
    /// </para>
    /// </summary>
    internal static class PluginLog
    {
        private const string MarkerFileName = "verbose.txt";

        private static readonly bool Verbose = MarkerExists();

        private static bool MarkerExists()
        {
            try
            {
                string dir = AssemblyDirectory();
                return dir != null && File.Exists(Path.Combine(dir, MarkerFileName));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Directory this assembly was loaded from.
        ///
        /// <para>
        ///   <c>Assembly.Location</c> is empty for assemblies loaded from memory, and a host is
        ///   free to do that. Falling back to <c>CodeBase</c> costs five lines and removes a
        ///   failure mode where the diagnostic switch silently does nothing — which is the worst
        ///   possible way for a diagnostic switch to fail.
        /// </para>
        /// </summary>
        private static string AssemblyDirectory()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            string location = assembly.Location;
            if (!string.IsNullOrEmpty(location))
            {
                return Path.GetDirectoryName(location);
            }

            string codeBase = assembly.CodeBase;
            if (!string.IsNullOrEmpty(codeBase))
            {
                return Path.GetDirectoryName(new Uri(codeBase).LocalPath);
            }

            return null;
        }

        public static void Trace(ILogger logger, string message, params object[] args)
        {
            if (logger == null)
            {
                return;
            }
            if (Verbose)
            {
                logger.LogWarning("[tessoa] " + message, args);
            }
            else
            {
                logger.LogDebug("[tessoa] " + message, args);
            }
        }
    }
}
