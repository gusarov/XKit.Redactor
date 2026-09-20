using System.Text.Json;

namespace XKit.Redactor.Tests;

public class EntropyRedactorTests
{
	private const string Hidden = RedactorOptions.DefaultMaskToken;

	private static readonly WordDictionary _dictionary = new();

	private readonly EntropyRedactor _redactor = new(_dictionary);

	[Test]
	public void Should_redact_obvious_guid()
	{
		var value = _redactor.Redact("something", "3f2504e0-4f89-11d3-9a0c-0305e82c3301");
		Assert.That(value, Does.Not.Contain("0305e82c3301"));
	}

	[Test]
	[TestCase("source\\repos\\Quotaly\\.vs\\Quotaly\\config\\applicationhost.config")]
	[TestCase("C:\\ProgramData")]
	[TestCase("C:\\Users\\xkip\\source\\repos\\Quotaly\\Quotaly\\Quotaly.Web\\obj\\Debug\\net9.0\\ApiEndpoints.json")]
	[TestCase("MongoDBStorage connected to MongoDB, 12 documents")]
	public void Should_not_redact(string value)
	{
		Assert.That(_redactor.Redact(value), Is.EqualTo(value));
	}

	[Test]
	[TestCase("t8Leqdp6fQJSEeJDfWpZiZov4XBow9Gw1CDLqCTkWltxnaKD70kcnI5vZMuvloAYPc9p1uK0CuMxzDcr3PUypMEK9WnnTyfL2Ix+Juh72yuWVvBD0BXtm8Z8erzYjm+zMvtG8IGjDuF0zu+97poY3VFXnkCu6wIs/4v/hgdaH9NniEpUWkY0ZfUwmtz6j1qFJJsh1v0SaQjN9Bjvwvx+2+5hnvFARKVUc0rZ4zBRBHAyqVIs5XP12NsQx7A2JeBae6+sawda/eJT2ERV/ANcUmIM+umWEVP16/Ao8bJI0W8stFrmIBnbf9igYI+GiSKaLCdN2o+Q0j55hkoIA5joY9uzhlkuicsaeED3ux06RihQo6tAwspFbfyHiP4ySswAOSiJT3fo6N4+u+k2")]
	[TestCase("0J2Kz/NoagiRZrCh6+RoJvClGNmAnwlyzMjF1Pf5Rc/WttludTkZ7qxDe1PNrfkKscsaEUhFcHAQEo6pap6mS6+IjQvMwakTntvq8yAFRUFVP4e8wLzoifqv2xoIK1EB9opzLV9lsT1lwz/glmcS24dggoWqbXwvA7fnYzLD7M18oA+mGUgGkkBME22Z3Dtoxa6ZYFkzxYsb7xYklrBSmI1utjoCUyvKUrBuwFsZpO1yi/N9q40IG9T1RFI+8lILAo8MdzknltEAh8DVilQM3C0AuEYOV6QZ0ojbeFUxtQh94Yb8CI3qAKlMvkYwPXDlk0m+dlIdXndtvLHBf3imhuZ8TeapJMtjvKsu5rAJ4YuiovCDmDrksefI/I8SKtjeWVrRf4e/p1TEpGuv")]
	[TestCase("w5yaHEJ4MGGWSS1jUkLNZQl1Ffw/bGN+c5DywfPoKf7Cl5toEFBDZmmidvPE2WWDxbC/13+peQPXB1xFYbvSypptrFOP1xiCIZuXtymt7GlNSjcs3ffOGkBlZNerIO4d+3qLGHDXXzdcscEXTPDIiH/BWmZZ2uRQp7oBk/4lYkbysqg7bkIMmQyBa3k/r3QuLwb7qHqS1sP0F0aW1ZAYIBbtG+7x/XYgbJRw8tpoqCZhSeBXmXZQeEQOmFoN09x83zj0n0ahhfFA+lcXM55xWkR7I4Y1VaZJoOVpzfU1wEe9a/vwqfVyfAxkji36KpMPaiINbNiyophXFwDB68UYp8aijPn6JSuF38hSxLptB4m787n5Vksh1AbMB+sRciAefb2caOh+fm1mbn0d")]
	public void Should_redact(string value)
	{
		Assert.That(_redactor.Redact(value), Is.EqualTo(Hidden));
	}

	[Test]
	[Ignore("Sometimes it still fails, if you want to investigate - hit run until failure, should not take longer than 15")]
	public void Should_redact_base64()
	{
		var buf = new byte[300];
		for (int i = 0; i < 100; i++)
		{
			Random.Shared.NextBytes(buf);
			var bigBase64 = Convert.ToBase64String(buf, Base64FormattingOptions.None);
			Console.WriteLine(bigBase64);
			Assert.That(_redactor.Redact(bigBase64), Is.EqualTo(Hidden));
		}
	}

	/// <summary>
	/// A dump of a real development environment - every key/value a config page would render -
	/// with the expected output beside each one that changes. This is the regression suite the
	/// original was tuned against; a change that moves any of these needs a reason.
	/// </summary>
	[Test]
	[TestCaseSource(typeof(ConfigTestDataSource), nameof(ConfigTestDataSource.DefaultConfigurationSources))]
	public void Should_redact_config_sources(string key, string value, string? expected)
	{
		var redacted = _redactor.Redact(value, key);
		if (redacted == "???")
		{
			Assert.Fail($"Please provide value for {key} = {value}");
		}
		else if (expected == "***") // the fixture's shorthand for "the whole value is hidden"
		{
			Assert.That(redacted, Is.EqualTo(Hidden));
		}
		else
		{
			Assert.That(redacted, Is.EqualTo(expected));
		}
	}

	[Test]
	public void Should_lower_the_bar_for_a_credential_shaped_key()
	{
		// Not random enough to be hidden on its own, random enough once the key says "password".
		const string value = "Xk9pQ2mZ";
		Assert.Multiple(() =>
		{
			Assert.That(_redactor.Redact(value), Is.EqualTo(value));
			Assert.That(_redactor.Redact(value, "Kestrel:Certificates:Development:Password"), Is.EqualTo(Hidden));
		});
	}

	[Test]
	public void Should_reveal_the_ends_of_each_hidden_token_when_masking()
	{
		var options = new RedactorOptions { Mode = RedactionMode.Mask };
		var redacted = _redactor.Redact("valu=cc7e92c1-2f90-414f-86a2-8dfaec50e181", options);
		Assert.That(redacted, Is.EqualTo($"valu=cc7{Hidden}181"));
	}

	[Test]
	public void Should_mask_a_uri_password_rather_than_erase_it_when_asked()
	{
		var options = new RedactorOptions { Mode = RedactionMode.Mask };
		var redacted = _redactor.Redact("mongodb://bob:8dfaec50e181fedcba@localhost/db", options);
		Assert.That(redacted, Is.EqualTo($"mongodb://bob:8df{Hidden}cba@localhost/db"));
	}

	[Test]
	public void Should_use_the_callers_mask_token()
	{
		var options = new RedactorOptions { MaskToken = "***" };
		Assert.That(_redactor.Redact("5b909a45-86fd-4e94-9c8e-4f396fdf6324", options), Is.EqualTo("***"));
	}

	[Test]
	public void Should_share_a_dictionary_between_instances_without_changing_the_answer()
	{
		const string value = "mongodb://bob:8dfaec50e181@localhost/quotaly-auth";
		var own = new EntropyRedactor();
		Assert.That(own.Redact(value), Is.EqualTo(_redactor.Redact(value)));
	}

	[Test]
	public void Should_treat_extra_words_as_prose()
	{
		// A product code reads as random until the host says it is a word. Digits between the
		// letters keep any three-letter dictionary word from hiding inside it by accident.
		const string value = "q7z3x9j2v5k1";
		var knowing = new EntropyRedactor(new WordDictionary([value]));
		Assert.Multiple(() =>
		{
			Assert.That(_redactor.Redact(value), Is.EqualTo(Hidden));
			Assert.That(knowing.Redact(value), Is.EqualTo(value));
		});
	}

	[Test]
	public void Should_redact_a_log_line_in_bulk_without_dominating_the_pipeline()
	{
		// The original did one string.Replace per dictionary word per token - ~10,000 allocations a
		// call - which was fine for an admin page and unusable in a log sink. This is a coarse guard
		// against that coming back, not a benchmark: the bound is two orders of magnitude above what
		// the single-pass version needs.
		const string line = "MongoDBStorage connected to MongoDB mongodb+srv://user:p%40ssw0rd@mongo.xkit.tools/xkit_apextroid?appName=apex, cursor 5b909a45-86fd-4e94-9c8e-4f396fdf6324 advanced by 12";
		var watch = System.Diagnostics.Stopwatch.StartNew();
		for (var i = 0; i < 1_000; i++)
		{
			_redactor.Redact(line);
		}

		watch.Stop();
		Assert.That(watch.ElapsedMilliseconds, Is.LessThan(2_000), "1,000 log lines");
	}
}

public static class ConfigTestDataSource
{
	public static IEnumerable<TestCaseData> DefaultConfigurationSources()
	{
		Dictionary<string, string> dic;
		try
		{
			dic = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText("testconfigdata.json"))!;
		}
		catch (Exception ex)
		{
			dic = new Dictionary<string, string>
			{
				["Exception"] = ex.ToString(),
			};
		}

		foreach (var item in dic)
		{
			if (!item.Key.EndsWith('$'))
			{
				if (!dic.TryGetValue(item.Key + "$", out var expected))
				{
					expected = item.Value;
				}

				yield return new TestCaseData(item.Key, item.Value, expected);
			}
		}
	}
}
