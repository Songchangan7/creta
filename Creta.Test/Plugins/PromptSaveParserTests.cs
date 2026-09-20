using Creta.Plugin.LocalPromptSearch;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Creta.Test.Plugins;

public class PromptSaveParserTests
{
    [Test]
    public void GivenTypedTextAndTagsWhenSplitThenTypedTextIsContent()
    {
        var split = PromptSaveParser.SplitSearch("你好世界 #办公 #总结");

        ClassicAssert.AreEqual("你好世界", split.TypedContent);
        CollectionAssert.AreEqual(new[] { "办公", "总结" }, split.Tags);
    }

    [Test]
    public void GivenTypedTextWhenParseThenTitleComesFromContent()
    {
        var input = PromptSaveParser.Parse("你好 #办公", "你好", true);

        ClassicAssert.AreEqual("你好", input.Title);
        ClassicAssert.IsTrue(input.ReplaceTitle);
        CollectionAssert.AreEqual(new[] { "办公" }, input.Tags);
        CollectionAssert.Contains(input.Keywords, "你好");
        CollectionAssert.Contains(input.Keywords, "办公");
    }

    [Test]
    public void GivenTagsOnlyWhenParseThenUsesContentTitle()
    {
        var input = PromptSaveParser.Parse("#办公", "请把这段会议记录整理成纪要和待办。", false);

        ClassicAssert.AreEqual("请把这段会议记录整理成纪要和待办。", input.Title);
        ClassicAssert.IsFalse(input.ReplaceTitle);
        CollectionAssert.AreEqual(new[] { "办公" }, input.Tags);
    }

    [Test]
    public void GivenEmptySearchWhenParseThenTruncatesFirstLine()
    {
        var firstLine = "这是一段明显超过二十四字限制的提示词正文第一行不要出现";
        var content = firstLine + "\n第二行不该出现在标题里";
        var input = PromptSaveParser.Parse("   ", content, false);

        ClassicAssert.AreEqual(firstLine[..PromptSaveParser.AutoTitleMaxLength] + "…", input.Title);
        ClassicAssert.IsFalse(input.ReplaceTitle);
        ClassicAssert.IsEmpty(input.Tags);
    }

    [Test]
    public void GivenShortClipboardWhenValidateThenFails()
    {
        ClassicAssert.IsFalse(PromptSaveParser.IsContentValid("啊", out var error));
        ClassicAssert.IsNotEmpty(error);
    }

    [Test]
    public void GivenTwoCharactersWhenValidateThenSucceeds()
    {
        ClassicAssert.IsTrue(PromptSaveParser.IsContentValid("你好", out var error));
        ClassicAssert.IsEmpty(error);
    }

    [Test]
    public void GivenEmptyContentWhenValidateThenFails()
    {
        ClassicAssert.IsFalse(PromptSaveParser.IsContentValid("   ", out var error));
        StringAssert.Contains("没有可保存的文本", error);
    }

    [Test]
    public void GivenLongContentWhenPreviewThenTruncates()
    {
        var preview = PromptSaveParser.Preview(new string('测', 120), 80);

        ClassicAssert.AreEqual(81, preview.Length);
        StringAssert.EndsWith("…", preview);
    }
}
