using System;
using System.Reflection;
using FolderTools.Core;

namespace FolderTools.App
{
    /// <summary>Identity of the running build: version and the executable's own path.</summary>
    internal static class ProductInfo
    {
        private static Assembly Entry => Assembly.GetEntryAssembly() ?? typeof(ProductInfo).Assembly;

        public static string Version =>
            VersionUtil.Display(
                Entry.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                Entry.GetName().Version?.ToString(3));

        public static string ExecutablePath => Entry.Location;

        public static string VersionText => "FolderTools " + Version;
    }
}
