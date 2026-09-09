using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace Creta.Infrastructure.UserSettings
{
    public static class QueryShortcutHelper
    {
        public const int CurrentDefaultQueryShortcutsVersion = 1;
        public const string ChatGptKey = "chatgpt";
        public const string ChatGptUrl = "https://chatgpt.com";

        public static readonly IReadOnlyList<(string Key, string Value)> DefaultCustomShortcuts =
        [
            (ChatGptKey, ChatGptUrl)
        ];

        /// <summary>
        /// Seed built-in query shortcuts for new and existing Settings.json files.
        /// Existing keys are left unchanged so a user-deleted shortcut stays deleted after the version bump.
        /// </summary>
        public static bool EnsureDefaultCustomShortcuts(Settings settings)
        {
            if (settings == null)
            {
                return false;
            }

            settings.CustomShortcuts ??= new ObservableCollection<CustomShortcutModel>();

            if (settings.DefaultQueryShortcutsVersion >= CurrentDefaultQueryShortcutsVersion)
            {
                return false;
            }

            foreach (var (key, value) in DefaultCustomShortcuts)
            {
                if (settings.CustomShortcuts.Any(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                settings.CustomShortcuts.Add(new CustomShortcutModel(key, value));
            }

            settings.DefaultQueryShortcutsVersion = CurrentDefaultQueryShortcutsVersion;
            return true;
        }

        /// <summary>
        /// Expand custom query shortcuts. A key matching the entire query is replaced;
        /// <c>@key</c> can expand inside a longer query.
        /// </summary>
        public static string ExpandCustomShortcuts(string queryText, IEnumerable<CustomShortcutModel> customShortcuts)
        {
            if (string.IsNullOrEmpty(queryText) || customShortcuts == null)
            {
                return queryText;
            }

            var queryBuilder = new StringBuilder(queryText);
            foreach (var shortcut in customShortcuts.OrderByDescending(x => x.Key?.Length ?? 0))
            {
                if (string.IsNullOrEmpty(shortcut.Key))
                {
                    continue;
                }

                var expansion = shortcut.Expand?.Invoke() ?? shortcut.Value;
                if (queryBuilder.ToString().Equals(shortcut.Key, StringComparison.Ordinal))
                {
                    queryBuilder.Replace(shortcut.Key, expansion);
                }

                queryBuilder.Replace('@' + shortcut.Key, expansion);
            }

            return queryBuilder.ToString();
        }
    }
}
