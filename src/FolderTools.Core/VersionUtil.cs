using System;

namespace FolderTools.Core
{
    /// <summary>
    /// Turns the assembly's version attributes into the single string the tool shows
    /// and the build verifies.
    /// </summary>
    public static class VersionUtil
    {
        /// <summary>
        /// Prefers the informational version and drops the source-revision suffix the
        /// build appends ("0.1.0+<sha>"). Falls back to the assembly version when the
        /// informational one is absent, and to 0.0.0 when neither is usable.
        /// </summary>
        public static string Display(string? informationalVersion, string? assemblyVersion)
        {
            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                string value = informationalVersion!.Trim();
                int plus = value.IndexOf('+');
                if (plus > 0)
                {
                    value = value.Substring(0, plus);
                }

                if (value.Length > 0)
                {
                    return value;
                }
            }

            if (!string.IsNullOrWhiteSpace(assemblyVersion))
            {
                return assemblyVersion!.Trim();
            }

            return "0.0.0";
        }
    }
}
