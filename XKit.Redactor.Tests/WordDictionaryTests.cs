namespace XKit.Redactor.Tests;

public class WordDictionaryTests
{
	private static readonly WordDictionary _dictionary = new();

	[TestCase("password", true)]
	[TestCase("PASSWORD", true)]
	[TestCase("the", true)]
	[TestCase("dotnet", true)]
	[TestCase("2024", true)]
	[TestCase("zqxjvkwptyb", false)]
	[TestCase("", false)]
	public void Should_know_its_words_regardless_of_case(string word, bool expected)
	{
		Assert.That(_dictionary.Contains(word), Is.EqualTo(expected));
	}

	[TestCase("tokens", true)]
	[TestCase("token", true)]
	[TestCase("zqxjvkwptybs", false)]
	public void Should_accept_a_plural_of_a_known_word(string word, bool expected)
	{
		Assert.That(_dictionary.IsKnownWord(word), Is.EqualTo(expected));
	}

	[TestCase("thepassword123", "123")]
	[TestCase("PASSWORDthe", "")]
	[TestCase("xqzpasswordxqz", "xqzxqz")]
	[TestCase("xqz", "xqz")]
	[TestCase("", "")]
	public void Should_strip_known_words_that_ran_together(string source, string expected)
	{
		var buffer = new char[source.Length];
		var written = _dictionary.StripKnownWords(source, buffer);
		Assert.That(new string(buffer, 0, written), Is.EqualTo(expected));
	}

	[Test]
	public void Should_take_the_longest_word_at_each_position()
	{
		// "theater" is a word; taking "the" first would leave "ater" behind.
		var buffer = new char[7];
		var written = _dictionary.StripKnownWords("theater", buffer);
		Assert.That(written, Is.Zero);
	}

	[Test]
	public void Should_never_strip_a_two_letter_word()
	{
		// "an" and "is" occur inside random text far too often to mean anything.
		var buffer = new char[4];
		var written = _dictionary.StripKnownWords("anis", buffer);
		Assert.That(new string(buffer, 0, written), Is.EqualTo("anis"));
	}

	[Test]
	public void Should_include_what_the_host_adds()
	{
		var extended = new WordDictionary(["Poloniex", " apextroid "]);
		Assert.Multiple(() =>
		{
			Assert.That(extended.Contains("poloniex"), Is.True);
			Assert.That(extended.Contains("apextroid"), Is.True);
			Assert.That(extended.Count, Is.EqualTo(_dictionary.Count + 2));
		});
	}
}
