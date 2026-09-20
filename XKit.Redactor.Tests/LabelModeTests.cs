namespace XKit.Redactor.Tests;

/// <summary>
/// <see cref="RedactionMode.Label"/> - nothing of the value survives, and something the caller
/// knows is said instead, so "hidden because secret" and "hidden because broken" stop looking
/// identical to whoever reads the log.
/// </summary>
public class LabelModeTests
{
	private const string Hidden = RedactorOptions.DefaultMaskToken;
	private const string Secret = "Sup3rS3cretP4ss";

	private static readonly WordDictionary _dictionary = new();

	private readonly CredentialRedactor _credential = new();

	private static RedactorOptions Labelled(string? label = null, string? key = null, string? token = null)
	{
		return new RedactorOptions
		{
			Mode = RedactionMode.Label,
			Label = label,
			Key = key,
			MaskToken = token ?? Hidden,
		};
	}

	public static IEnumerable<TestCaseData> Redactors()
	{
		yield return new TestCaseData(new CredentialRedactor()).SetArgDisplayNames(nameof(CredentialRedactor));
		yield return new TestCaseData(new EntropyRedactor(_dictionary)).SetArgDisplayNames(nameof(EntropyRedactor));
	}

	/// <summary>
	/// Labels are caller-driven. No redactor authors one of its own - not the name of the rule that
	/// matched, not what tripped the detector, nothing. Where a rule matches inside a larger text
	/// the surrounding structure is preserved on purpose, so an invented label could only restate
	/// what is already on screen: <c>mongodb://●●●userinfo●●●@host</c> says nothing the
	/// <c>://…@</c> did not.
	/// </summary>
	[Test]
	[TestCaseSource(nameof(Redactors))]
	public void Should_not_invent_a_label(IRedactor redactor)
	{
		var options = Labelled();

		Assert.Multiple(() =>
		{
			Assert.That(redactor.Redact($"mongodb://apex:{Secret}@mongo.xkit.tools/db", options), Is.EqualTo(redactor.Redact($"mongodb://apex:{Secret}@mongo.xkit.tools/db")));
			Assert.That(redactor.Redact($"Server=db;Password={Secret}", options), Is.EqualTo(redactor.Redact($"Server=db;Password={Secret}")));
			Assert.That(redactor.Redact($"{{ \"ApiKey\": \"{Secret}\" }}", options), Is.EqualTo(redactor.Redact($"{{ \"ApiKey\": \"{Secret}\" }}")));
		});
	}

	[Test]
	public void Should_be_exactly_erase_when_the_caller_names_nothing()
	{
		var text = $"mongodb://apex:{Secret}@mongo.xkit.tools/db and Password={Secret}";

		Assert.That(_credential.Redact(text, Labelled()), Is.EqualTo(_credential.Redact(text)));
	}

	[Test]
	public void Should_prefer_an_explicit_label_over_the_key()
	{
		var text = $"Server=db;Password={Secret}";

		Assert.Multiple(() =>
		{
			Assert.That(_credential.Redact(text, Labelled(label: "chosen", key: "Some:Key")), Does.Contain("●●●chosen●●●"));
			Assert.That(_credential.Redact(text, Labelled(key: "Some:Key")), Does.Contain("●●●Some:Key●●●"), "the key the caller passed is still the caller's");
		});
	}

	[Test]
	[TestCaseSource(nameof(Redactors))]
	public void Should_keep_no_character_of_the_value(IRedactor redactor)
	{
		var redacted = redactor.Redact($"mongodb://apex:{Secret}@mongo.xkit.tools/db", Labelled(label: "MongoConfigurationException"));

		Assert.Multiple(() =>
		{
			Assert.That(redacted, Does.Not.Contain(Secret));
			Assert.That(redacted, Does.Not.Contain("Sup"), "not the head either");
			Assert.That(redacted, Does.Not.Contain("4ss"), "nor the tail");
			Assert.That(redacted, Does.Contain("MongoConfigurationException"));
		});
	}

	[Test]
	public void Should_make_two_values_under_the_same_label_indistinguishable()
	{
		// Otherwise the mode would leak by comparison what it refuses to leak directly.
		var options = Labelled(label: "apikey", key: "Poloniex:ApiKey");

		Assert.That(
			_credential.Redact("a", options)
			, Is.EqualTo(_credential.Redact("a very much longer secret indeed", options))
		);
	}

	/// <summary>
	/// The reason the null/empty branch tests <c>Mode == Mask</c> rather than <c>Mode == Erase</c>:
	/// Mask is the only mode that passes anything through, so everything else hides - and a label
	/// that resolves must be used even when there was no value, or the output would say "unset"
	/// where it says "hidden" for a real one, which is the leak the contract exists to prevent.
	/// </summary>
	[Test]
	public void Should_not_let_an_unset_value_be_told_from_a_set_one()
	{
		var options = Labelled(key: "Poloniex:ApiKey");
		var forARealKey = _credential.Redact("abcdefghijklmnop", options);

		Assert.Multiple(() =>
		{
			Assert.That(forARealKey, Is.EqualTo("●●●Poloniex:ApiKey●●●"));
			Assert.That(_credential.Redact(null, options), Is.EqualTo(forARealKey), "a null must not be tellable from a key");
			Assert.That(_credential.Redact("", options), Is.EqualTo(forARealKey), "nor an empty string");
		});
	}

	[Test]
	public void Should_fall_back_to_erase_when_no_label_resolves()
	{
		// Nothing to say, so say nothing - not an empty pair of token halves.
		Assert.Multiple(() =>
		{
			Assert.That(_credential.Redact(null, Labelled()), Is.EqualTo(Hidden));
			Assert.That(_credential.Redact("", Labelled()), Is.EqualTo(Hidden));
			Assert.That(new RedactorOptions { Mode = RedactionMode.Label }.Hide(Secret), Is.EqualTo(Hidden));
			Assert.That(Labelled(label: "").Hide(Secret), Is.EqualTo(Hidden), "an empty label is no label");
		});
	}

	[Test]
	[TestCase("", "x")]
	[TestCase("*", "x")] // one character cannot be halved, so nothing wraps it
	[TestCase("**", "*x*")]
	[TestCase("***", "*x*")]
	[TestCase("****", "**x**")]
	[TestCase("******", "***x***")]
	[TestCase("********", "***x***")]
	public void Should_not_slice_past_the_end_of_a_short_token(string token, string expected)
	{
		// Half the token each side, capped at three. A caller shortening the token must not throw.
		Assert.That(Labelled(label: "x", token: token).Hide(Secret), Is.EqualTo(expected));
	}

	[Test]
	public void Should_hide_a_whole_value_the_caller_has_already_isolated()
	{
		// The case the mode was asked for: a connection string that will not parse, so there is
		// nothing safe to say about it and nothing for a rule to match either. The caller knows the
		// reason; the redactor could never work it out.
		var reason = new FormatException().GetType().Name;
		var described = Labelled(label: reason).Hide("mongodb://not even close to valid", isolated: false);

		Assert.That(described, Is.EqualTo("●●●FormatException●●●"));
	}

	[Test]
	public void Should_use_the_same_label_for_every_secret_in_one_text()
	{
		// A label says why the caller is hiding things, and that reason does not change halfway
		// along the line. What distinguishes the two here is the context each one sits in, which the
		// rules preserve - not the label.
		var redacted = _credential.Redact($"mongodb://apex:{Secret}@host/db and pwd={Secret}", Labelled(label: "unparseable"));

		Assert.That(redacted, Is.EqualTo("mongodb://●●●unparseable●●●@host/db and pwd=●●●unparseable●●●"));
	}

	[Test]
	public void Should_use_the_callers_token_around_the_label()
	{
		// Three asterisks halve to one per side, so the label is wrapped in what the caller chose.
		Assert.That(
			_credential.Redact($"Password={Secret}", Labelled(label: "password", token: "***"))
			, Is.EqualTo("Password=*password*")
		);
	}

	[Test]
	public void Should_be_mutually_exclusive_with_mask()
	{
		// One enum, so the compiler already enforces it - this pins the consequence, that a label
		// and a partial reveal never appear together.
		var masked = _credential.Redact($"Password={Secret}", new RedactorOptions { Mode = RedactionMode.Mask, Label = "password" });

		Assert.Multiple(() =>
		{
			Assert.That(masked, Does.Not.Contain("password●●●"), "the label is ignored in Mask mode");
			Assert.That(masked, Is.EqualTo($"Password=Sup{Hidden}4ss"));
		});
	}
}
