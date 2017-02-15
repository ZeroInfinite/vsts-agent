using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    [ServiceLocator(Default = typeof(VarUtil))]
    public interface IVarUtil : IAgentService
    {
        void PrependPath(string directory);
        void SetEnvironmentVariable(string name, string value);
    }

    public sealed class VarUtil : AgentService, IVarUtil
    {
        public void PrependPath(string directory)
        {
            ArgUtil.Directory(directory, nameof(directory));

            // Build the new value.
            string path = Environment.GetEnvironmentVariable(Constants.PathVariable);
            if (string.IsNullOrEmpty(path))
            {
                // Careful not to add a trailing separator if the PATH is empty.
                // On OSX/Linux, a trailing separator indicates that "current directory"
                // is added to the PATH, which is considered a security risk.
                path = directory;
            }
            else
            {
                path = directory + Path.PathSeparator + path;
            }

            // Update the PATH environment variable.
            Environment.SetEnvironmentVariable(Constants.PathVariable, path);
        }

        public void SetEnvironmentVariable(string name, string value)
        {
            ArgUtil.NotNullOrEmpty(name, nameof(name));
            Environment.SetEnvironmentVariable(name, value);
        }

        public static string GetEnvironmentVariable(string name, bool forceInsensitive = false)
        {
            ArgUtil.NotNullOrEmpty(name, nameof(name));
            if (forceInsensitive)
            {
                return Environment.GetEnvironmentVariables().Keys
                    .Where(x => string.Equals(name, x, StringComparison.OrdinalIgnoreCase))
                    .Select(x => Environment.GetEnvironmentVariable(x))
                    .FirstOrDefault();
            }

            return Environment.GetEnvironmentVariable(name);
        }

        public static void ExpandEnvironmentVariables(IHostContext context, IDictionary<string, string> target)
        {
            ArgUtil.NotNull(context, nameof(context));
            Tracing trace = context.GetTrace(nameof(VarUtil));
            trace.Entering();

            // Determine which string comparer to use for the environment variable dictionary.
            StringComparer comparer;
            switch (Constants.Agent.Platform)
            {
                case Constants.OSPlatform.Linux:
                case Constants.OSPlatform.OSX:
                    comparer = StringComparer.CurrentCulture;
                    break;
                case Constants.OSPlatform.Windows:
                    comparer = StringComparer.CurrentCultureIgnoreCase;
                    break;
                default:
                    throw new NotSupportedException();
            }

            // Copy the environment variables into a dictionary that uses the correct comparer.
            var source = new Dictionary<string, string>(comparer);
            IDictionary environment = Environment.GetEnvironmentVariables();
            foreach (DictionaryEntry entry in environment)
            {
                string key = entry.Key as string ?? string.Empty;
                string val = entry.Value as string ?? string.Empty;
                source[key] = val;
            }

            // Expand the target values.
            ExpandValues(context, source, target);
        }

        public static void ExpandValues(IHostContext context, IDictionary<string, string> source, IDictionary<string, string> target)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(source, nameof(source));
            Tracing trace = context.GetTrace(nameof(VarUtil));
            trace.Entering();
            target = target ?? new Dictionary<string, string>();

            // This algorithm does not perform recursive replacement.

            // Process each key in the target dictionary.
            foreach (string targetKey in target.Keys.ToArray())
            {
                trace.Verbose($"Processing expansion for: '{targetKey}'");
                int startIndex = 0;
                int prefixIndex;
                int suffixIndex;
                string targetValue = target[targetKey] ?? string.Empty;

                // Find the next macro within the target value.
                while (startIndex < targetValue.Length &&
                    (prefixIndex = targetValue.IndexOf(Constants.Variables.MacroPrefix, startIndex, StringComparison.Ordinal)) >= 0 &&
                    (suffixIndex = targetValue.IndexOf(Constants.Variables.MacroSuffix, prefixIndex + Constants.Variables.MacroPrefix.Length, StringComparison.Ordinal)) >= 0)
                {
                    // A candidate was found.
                    string variableKey = targetValue.Substring(
                        startIndex: prefixIndex + Constants.Variables.MacroPrefix.Length,
                        length: suffixIndex - prefixIndex - Constants.Variables.MacroPrefix.Length);
                    trace.Verbose($"Found macro candidate: '{variableKey}'");
                    string variableValue;
                    if (!string.IsNullOrEmpty(variableKey) &&
                        TryGetValue(trace, source, variableKey, out variableValue))
                    {
                        // A matching variable was found.
                        // Update the target value.
                        trace.Verbose("Macro found.");
                        targetValue = string.Concat(
                            targetValue.Substring(0, prefixIndex),
                            variableValue ?? string.Empty,
                            targetValue.Substring(suffixIndex + Constants.Variables.MacroSuffix.Length));

                        // Bump the start index to prevent recursive replacement.
                        startIndex = prefixIndex + (variableValue ?? string.Empty).Length;
                    }
                    else
                    {
                        // A matching variable was not found.
                        trace.Verbose("Macro not found.");
                        startIndex = prefixIndex + 1;
                    }
                }

                target[targetKey] = targetValue ?? string.Empty;
            }
        }

        public static bool TryGetValue(Tracing trace, IDictionary<string, string> source, string name, out string val)
        {
            if (source.TryGetValue(name, out val))
            {
                val = val ?? string.Empty;
                trace.Verbose($"Get '{name}': '{val}'");
                return true;
            }

            val = null;
            trace.Verbose($"Get '{name}' (not found)");
            return false;
        }
    }
}