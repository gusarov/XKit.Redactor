namespace XKit.Redactor.Tests;

/// <summary>
/// <see cref="RedactionMode.Label"/> - nothing of the value survives, and something about its
/// context is said instead, so "hidden because secret" and "hidden because broken" stop looking
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

	[Test]
	public void Should_say_which_rule_matched_when_nobody_supplied_a_label()
	{
		// The reason it is a mode rather than a helper: every rule gets it, with no caller cooperation.
		Assert.Multiple(() =>
		{
			Assert.That(
				_credential.Redact($"mongodb://apex:{Secret}@mongo.xkit.tools/db", Labelled())
				, Is.EqualTo($"mongodb://●●●userinfo●●●@mongo.xkit.tools/db")
			);
			Assert.That(
				_credential.Redact($"Server=db;Password={Secret};Encrypt=true", Labelled())
				, Is.EqualTo("Server=db;Password=●●●password●●●;Encrypt=true")
			);
			Assert.That(
				_credential.Redact($"{{ \"ApiKey\": \"{Secret}\" }}", Labelled())
				, Is.EqualTo("{ \"ApiKey\": \"●●●apikey●●●\" }")
			);
		});
	}

	[Test]
	public void Should_resolve_the_label_as_label_then_key_then_rule_name()
	{
		var text = $"Server=db;Password={Secret}";

		Assert.Multiple(() =>
		{
			Assert.That(_credential.Redact(text, Labelled(label: "chosen", key: "Some:Key")), Does.Contain("●●●chosen●●●"), "an explicit label wins");
			Assert.That(_credential.Redact(text, Labelled(key: "Some:Key")), Does.Contain("●●●Some:Key●●●"), "then the key");
			Assert.That(_credential.Redact(text, Labelled()), Does.Contain("●●●password●●●"), "then the rule that matched");
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
		// The ApexTroid case: a connection string that will not parse, so there is nothing safe to
		// say about it and nothing for a rule to match either. The caller knows the reason; the
		// redactor could never work it out.
		var reason = new FormatException().GetType().Name;
		var described = Labelled(label: reason).Hide("mongodb://not even close to valid", isolated: false);

		Assert.That(described, Is.EqualTo("●●●FormatException●●●"));
	}

	[Test]
	public void Should_say_what_tripped_the_entropy_detector()
	{
		// Opaque today: it hides something the caller did not expect and cannot say why.
		var entropy = new EntropyRedactor(_dictionary);

		Assert.Multiple(() =>
		{
			Assert.That(entropy.Redact("5b909a45-86fd-4e94-9c8e-4f396fdf6324", Labelled()), Is.EqualTo("●●●guid●●●"));
			Assert.That(entropy.Redact("mongodb://bob:8dfaec50e181fedcba@localhost/db", Labelled()), Does.Contain("●●●uri-password●●●"));
		});
	}

	[Test]
	public void Should_apply_the_label_to_every_secret_found_independently()
	{
		var redacted = _credential.Redact($"pwd=First{Secret} and pwd=Second{Secret}", Labelled());

		Assert.That(redacted, Is.EqualTo("pwd=●●●password●●● and pwd=●●●password●●●"));
	}

	[Test]
	public void Should_use_the_callers_token_around_the_label()
	{
		// Three asterisks halve to one per side, so the label is wrapped in what the caller chose.
		Assert.That(
			_credential.Redact($"Password={Secret}", Labelled(token: "***"))
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
