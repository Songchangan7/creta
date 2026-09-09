using System.Collections.ObjectModel;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Creta.Infrastructure.UserSettings;

namespace Creta.Test
{
    public class QueryShortcutHelperTest
    {
        [Test]
        public void ExpandCustomShortcuts_ReplacesExactChatGptKeyWithOfficialWebUrl()
        {
            var shortcuts = new[]
            {
                new CustomShortcutModel(QueryShortcutHelper.ChatGptKey, QueryShortcutHelper.ChatGptUrl)
            };

            var expanded = QueryShortcutHelper.ExpandCustomShortcuts("chatgpt", shortcuts);

            ClassicAssert.AreEqual("https://chatgpt.com", expanded);
        }

        [Test]
        public void ExpandCustomShortcuts_ReplacesAtPrefixedKeyInsideQuery()
        {
            var shortcuts = new[]
            {
                new CustomShortcutModel(QueryShortcutHelper.ChatGptKey, QueryShortcutHelper.ChatGptUrl)
            };

            var expanded = QueryShortcutHelper.ExpandCustomShortcuts("open @chatgpt now", shortcuts);

            ClassicAssert.AreEqual("open https://chatgpt.com now", expanded);
        }

        [Test]
        public void ExpandCustomShortcuts_DoesNotReplaceKeyInsideOtherWords()
        {
            var shortcuts = new[]
            {
                new CustomShortcutModel(QueryShortcutHelper.ChatGptKey, QueryShortcutHelper.ChatGptUrl)
            };

            var expanded = QueryShortcutHelper.ExpandCustomShortcuts("chatgpt-plugin", shortcuts);

            ClassicAssert.AreEqual("chatgpt-plugin", expanded);
        }

        [Test]
        public void EnsureDefaultCustomShortcuts_AddsChatGptKeyForExistingSettings()
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
            ClassicAssert.AreEqual(QueryShortcutHelper.ChatGptKey, settings.CustomShortcuts[0].Key);
            ClassicAssert.AreEqual(QueryShortcutHelper.ChatGptUrl, settings.CustomShortcuts[0].Value);
        }

        [Test]
        public void EnsureDefaultCustomShortcuts_DoesNotDuplicateExistingChatGptKey()
        {
            var settings = new Settings
            {
                CustomShortcuts = new ObservableCollection<CustomShortcutModel>
                {
                    new("chatgpt", "https://chatgpt.com/?model=gpt-5")
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
