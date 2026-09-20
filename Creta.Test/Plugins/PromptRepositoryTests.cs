using System;
using System.IO;
using System.Linq;
using Creta.Plugin.LocalPromptSearch;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Creta.Test.Plugins;

public class PromptRepositoryTests
{
    private string _testRoot = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "Creta.PromptTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, true);
        }
    }

    [Test]
    public void GivenSampleFileWhenSaveThenWritesPromptsJson()
    {
        var pluginDirectory = CreatePluginDirectory(sampleOnly: true);
        var repository = new PromptRepository(pluginDirectory);
        repository.Load();

        var content = "请根据以下会议内容整理纪要和待办事项。";
        var input = PromptSaveParser.Parse("会议纪要 #办公", content, true);
        var prompt = repository.CreatePrompt(input, content);

        ClassicAssert.IsTrue(repository.TryAdd(prompt, string.Empty, out var error));
        ClassicAssert.IsEmpty(error);
        ClassicAssert.IsTrue(File.Exists(Path.Combine(pluginDirectory, "prompts.json")));
        StringAssert.EndsWith("prompts.json", repository.CurrentFilePath);

        repository.Load();
        var saved = repository.GetPrompts().FirstOrDefault(item => item.Content == content);
        ClassicAssert.IsNotNull(saved);
        ClassicAssert.AreEqual(content, saved!.Title);
        CollectionAssert.Contains(saved.Tags, "办公");
    }

    [Test]
    public void GivenExistingTitleWhenFindThenReturnsDuplicate()
    {
        var pluginDirectory = CreatePluginDirectory(sampleOnly: false);
        var repository = new PromptRepository(pluginDirectory);
        repository.Load();

        var existing = repository.FindByTitle("周报总结模板");
        ClassicAssert.IsNotNull(existing);
        ClassicAssert.AreEqual("weekly-report", existing!.Id);
    }

    [Test]
    public void GivenExistingPromptWhenUpdateThenOverwritesContent()
    {
        var pluginDirectory = CreatePluginDirectory(sampleOnly: false);
        var repository = new PromptRepository(pluginDirectory);
        repository.Load();

        var existing = repository.FindByTitle("周报总结模板");
        ClassicAssert.IsNotNull(existing);

        var input = PromptSaveParser.Parse("#办公", "请按新的结构重写这份周报，并列出风险。", false);
        repository.ApplyUpdate(existing!, input, "请按新的结构重写这份周报，并列出风险。");

        ClassicAssert.IsTrue(repository.TryUpdate(string.Empty, out var error));
        ClassicAssert.IsEmpty(error);

        repository.Load();
        var updated = repository.FindByTitle("周报总结模板");
        ClassicAssert.AreEqual("请按新的结构重写这份周报，并列出风险。", updated!.Content);
        CollectionAssert.Contains(updated.Tags, "办公");
    }

    [Test]
    public void GivenDuplicateTitleWhenCreateIdThenAddsSuffix()
    {
        var pluginDirectory = CreatePluginDirectory(sampleOnly: false);
        var repository = new PromptRepository(pluginDirectory);
        repository.Load();

        ClassicAssert.AreEqual("weekly-report-2", repository.CreateId("weekly-report"));
        ClassicAssert.AreEqual("周报总结模板", repository.CreateId("周报总结模板"));
    }

    private string CreatePluginDirectory(bool sampleOnly)
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        Directory.CreateDirectory(pluginDirectory);

        const string sampleJson =
            """
            [
              {
                "id": "weekly-report",
                "title": "周报总结模板",
                "description": "用于整理本周工作并生成周报",
                "tags": ["总结", "周报"],
                "keywords": ["周报"],
                "content": "请根据以下工作内容，帮我整理一份结构清晰、表达专业的周报：",
                "category": "办公",
                "favorite": true
              }
            ]
            """;

        File.WriteAllText(Path.Combine(pluginDirectory, "prompts.sample.json"), sampleJson);
        if (!sampleOnly)
        {
            File.WriteAllText(Path.Combine(pluginDirectory, "prompts.json"), sampleJson);
        }

        return pluginDirectory;
    }
}
