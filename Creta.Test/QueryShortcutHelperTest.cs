using System.Collections.ObjectModel;
using System.Linq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Creta.Infrastructure.UserSettings;

namespace Creta.Test
{
    public class QueryShortcutHelperTest
    {
        [Test]
        public void ExpandCustomShortcuts_DoesNotRewritePlainChatGptQuery()
        {
            var shortcuts = new[]
            {
                new CustomShortcutModel(QueryShortcutHelper.ChatGptWebKey, QueryShortcutHelper.ChatGptUrl)
            };

            var expanded = QueryShortcutHelper.ExpandCustomShortcuts("chatgpt", shortcuts);

            ClassicAssert.AreEqual("chatgpt", expanded);
        }

        [Test]
        public void ExpandCustomShortcuts_ReplacesWebKeyWithOfficialWebUrl()
        {
            var shortcuts = new[]
            {
                new CustomShortcutModel(QueryShortcutHelper.ChatGptWebKey, QueryShortcutHelper.ChatGptUrl)
            };

            var expanded = QueryShortcutHelper.ExpandCustomShortcuts("chatgpt网页", shortcuts);

            ClassicAssert.AreEqual("https://chatgpt.com", expanded);
        }

        [Test]
        public void ExpandCustomShortcuts_ReplacesAtPrefixedWebKeyInsideQuery()
        {
            var shortcuts = new[]
            {
                new CustomShortcutModel(QueryShortcutHelper.ChatGptWebKey, QueryShortcutHelper.ChatGptUrl)
            };

            var expanded = QueryShortcutHelper.ExpandCustomShortcuts("open @chatgpt网页 now", shortcuts);

            ClassicAssert.AreEqual("open https://chatgpt.com now", expanded);
        }

        [Test]
        public void ExpandCustomShortcuts_DoesNotReplaceKeyInsideOtherWords()
        {
            var shortcuts = new[]
            {
                new CustomShortcutModel(QueryShortcutHelper.ChatGptWebKey, QueryShortcutHelper.ChatGptUrl)
            };

            var expanded = QueryShortcutHelper.ExpandCustomShortcuts("chatgpt网页-plugin", shortcuts);

            ClassicAssert.AreEqual("chatgpt网页-plugin", expanded);
        }

        [Test]
        public void EnsureDefaultCustomShortcuts_AddsWebKeyAndLeavesDesktopKeyFree()
        {
            var settings = new Settings
            {
                CustomShortcuts = new ObservableCollection<CustomShortcutModel>(),
                DefaultQueryShortcutsVersion = 0
            };

            var changed = QueryShortcutHelper.EnsureDefaultCustomShortcuts(settings);

            ClassicAssert.IsTrue(changed);
            ClassicAssert.AreEqual(QueryShortcutHelper.CurrentDefaultQueryShortcutsVersion, settings.DefaultQueryShortcutsVersion);
            ClassicAssert.AreEqual(1, settings.CustomShortcuts.Count);
            ClassicAssert.AreEqual(QueryShortcutHelper.ChatGptWebKey, settings.CustomShortcuts[0].Key);
            ClassicAssert.AreEqual(QueryShortcutHelper.ChatGptUrl, settings.CustomShortcuts[0].Value);
            ClassicAssert.IsFalse(settings.CustomShortcuts.Any(item => item.Key == QueryShortcutHelper.ChatGptDesktopKey));
        }

        [Test]
        public void EnsureDefaultCustomShortcuts_MigratesLegacyChatGptWebsiteShortcutToWebKey()
        {
            var settings = new Settings
            {
                CustomShortcuts = new ObservableCollection<CustomShortcutModel>
                {
                    new(QueryShortcutHelper.LegacyChatGptWebKey, QueryShortcutHelper.ChatGptUrl)
                },
                DefaultQueryShortcutsVersion = 1
            };

            var changed = QueryShortcutHelper.EnsureDefaultCustomShortcuts(settings);

            ClassicAssert.IsTrue(changed);
            ClassicAssert.AreEqual(1, settings.CustomShortcuts.Count);
            ClassicAssert.AreEqual(QueryShortcutHelper.ChatGptWebKey, settings.CustomShortcuts[0].Key);
            ClassicAssert.AreEqual(QueryShortcutHelper.ChatGptUrl, settings.CustomShortcuts[0].Value);
        }

        [Test]
        public void EnsureDefaultCustomShortcuts_KeepsCustomizedChatGptShortcut()
        {
            var settings = new Settings
            {
                CustomShortcuts = new ObservableCollection<CustomShortcutModel>
                {
                    new("chatgpt", "https://chatgpt.com/?model=gpt-5")
                },
                DefaultQueryShortcutsVersion = 1
            };

            var changed = QueryShortcutHelper.EnsureDefaultCustomShortcuts(settings);

            ClassicAssert.IsTrue(changed);
            ClassicAssert.AreEqual(2, settings.CustomShortcuts.Count);
            ClassicAssert.AreEqual("https://chatgpt.com/?model=gpt-5", settings.CustomShortcuts[0].Value);
            ClassicAssert.AreEqual(QueryShortcutHelper.ChatGptWebKey, settings.CustomShortcuts[1].Key);
        }

        [Test]
        public void EnsureDefaultCustomShortcuts_DoesNotDuplicateExistingWebKey()
        {
            var settings = new Settings
            {
                CustomShortcuts = new ObservableCollection<CustomShortcutModel>
                {
                    new(QueryShortcutHelper.ChatGptWebKey, "https://chatgpt.com/?model=gpt-5")
                },
                DefaultQueryShortcutsVersion = 0
            };

            var changed = QueryShortcutHelper.EnsureDefaultCustomShortcuts(settings);

            ClassicAssert.IsTrue(changed);
            ClassicAssert.AreEqual(1, settings.CustomShortcuts.Count);
            ClassicAssert.AreEqual("https://chatgpt.com/?model=gpt-5", settings.CustomShortcuts[0].Value);
        }

        [Test]
        public void EnsureDefaultCustomShortcuts_DoesNotRestoreDeletedShortcutAfterVersionBump()
        {
            var settings = new Settings
            {
                CustomShortcuts = new ObservableCollection<CustomShortcutModel>(),
                DefaultQueryShortcutsVersion = QueryShortcutHelper.CurrentDefaultQueryShortcutsVersion
            };

            var changed = QueryShortcutHelper.EnsureDefaultCustomShortcuts(settings);

            ClassicAssert.IsFalse(changed);
            ClassicAssert.IsEmpty(settings.CustomShortcuts);
        }
    }
}
