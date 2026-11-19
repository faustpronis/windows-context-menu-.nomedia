using System;
using System.Collections.Generic;
using System.Linq;

namespace FolderTools.Core
{
    /// <summary>A destination offered for the current selection, plus the items routed to it.</summary>
    public sealed class DestinationMatch
    {
        public DestinationMatch(Destination destination, IReadOnlyList<string> files)
        {
            Destination = destination;
            Files = files;
        }

        public Destination Destination { get; }

        /// <summary>Selected items whose extension routes them to this destination.</summary>
        public IReadOnlyList<string> Files { get; }
    }

    /// <summary>
    /// Decides which destinations apply to a selection.
    ///
    /// The shell shows the intersection of verbs for a multi-file selection, so the
    /// tool never relies on it: the menu lists every configured destination and the
    /// app routes each selected file by its own extension. That is also why a
    /// destination appears when only some of the selected files match it.
    /// </summary>
    public static class DestinationResolver
    {
        /// <summary>
        /// Destinations matching at least one selected item, in configuration order.
        /// Items that match nothing are simply not routed anywhere.
        /// </summary>
        public static IReadOnlyList<DestinationMatch> Resolve(
            IEnumerable<Destination> destinations,
            IEnumerable<string> selectedItems,
            Func<string, bool>? isDirectory = null)
        {
            if (destinations == null)
            {
                throw new ArgumentNullException(nameof(destinations));
            }

            Func<string, bool> directoryCheck = isDirectory ?? (_ => false);
            List<string> items = (selectedItems ?? Enumerable.Empty<string>())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .ToList();

            var matches = new List<DestinationMatch>();
            foreach (Destination destination in destinations)
            {
                if (destination == null || !destination.IsEnabled || string.IsNullOrWhiteSpace(destination.Path))
                {
                    continue;
                }

                List<string> routed = items
                    .Where(item => destination.MatchesPath(item, directoryCheck(item)))
                    .ToList();

                if (routed.Count > 0)
                {
                    matches.Add(new DestinationMatch(destination, routed));
                }
            }

            return matches;
        }

        /// <summary>
        /// Every destination for which at least one selected item matches, merged across
        /// extensions. Selecting a.txt and b.jpg yields both destinations when each is
        /// configured, which a static shell verb cannot express.
        /// </summary>
        public static IReadOnlyList<Destination> DestinationsFor(IEnumerable<Destination> destinations, IEnumerable<string> selectedItems)
        {
            return Resolve(destinations, selectedItems).Select(m => m.Destination).ToList();
        }

        /// <summary>
        /// Extension of a file or folder name, including the dot. A directory returns
        /// empty, so a pattern only reaches folders when it is written to match "".
        /// </summary>
        public static string GetExtension(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string name = path!.Trim().Trim('"');
            int slash = name.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0)
            {
                name = name.Substring(slash + 1);
            }

            int dot = name.LastIndexOf('.');
            return dot <= 0 || dot == name.Length - 1 ? string.Empty : name.Substring(dot);
        }
    }
}
