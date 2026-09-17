using NUnit.Framework;
using NUnit.Framework.Legacy;
using Creta.Infrastructure;

namespace Creta.Test.Infrastructure;

public class SelectedTextFormatterTests
{
    [Test]
    public void GivenPlainTextWhenFormatThenKeepsContent()
    {
        ClassicAssert.AreEqual("hello world", SelectedTextFormatter.ForQuery("hello world"));
    }

    [Test]
    public void GivenMultilineTextWhenFormatThenCollapsesWhitespace()
    {
        ClassicAssert.AreEqual("line one line two", SelectedTextFormatter.ForQuery("line one\r\n\tline two"));
    }

    [Test]
    public void GivenBlankTextWhenFormatThenReturnsEmpty()
    {
        ClassicAssert.AreEqual(string.Empty, SelectedTextFormatter.ForQuery("   \r\n  "));
        ClassicAssert.AreEqual(string.Empty, SelectedTextFormatter.ForQuery(null));
    }

    [Test]
    public void GivenLongTextWhenFormatThenTruncatesToMaxLength()
    {
        var text = new string('a', SelectedTextFormatter.MaxQueryLength + 25);

        var formatted = SelectedTextFormatter.ForQuery(text);

        ClassicAssert.AreEqual(SelectedTextFormatter.MaxQueryLength, formatted.Length);
    }

    [Test]
    public void GivenSelectedTextWhenBuildNoteQueryThenPrefixesActionKeyword()
    {
        ClassicAssert.AreEqual("n 这段文本", SelectedTextFormatter.BuildNoteQuery("n", "这段文本"));
        ClassicAssert.AreEqual(string.Empty, SelectedTextFormatter.BuildNoteQuery("n", "   "));
        ClassicAssert.AreEqual(string.Empty, SelectedTextFormatter.BuildNoteQuery("", "这段文本"));
    }
}
