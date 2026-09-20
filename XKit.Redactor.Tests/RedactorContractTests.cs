namespace XKit.Redactor.Tests;

/// <summary>
/// What every <see cref="IRedactor"/> promises, whichever way it finds secrets. A new
/// implementation goes into <see cref="Redactors"/> and inherits the whole contract.
/// </summary>
public class RedactorContractTests
{
	private const string Hidden = RedactorOptions.DefaultMaskToken;

	public static IEnumerable<TestCaseData> Redactors()
	{
		yield return new TestCaseData(new CredentialRedactor()).SetArgDisplayNames(nameof(CredentialRedactor));
		yield return new TestCaseData(new EntropyRedactor()).SetArgDisplayNames(nameof(EntropyRedactor));
	}

	[TestCaseSource(nameof(Redactors))]
	public void Should_answer_erase_with_the_token_for_null_and_empty(IRedactor redactor)
	{
		// So a log line can never be read to mean "this one is not configured".
		Assert.Multiple(() =>
		{
			Assert.That(redactor.Redact(null), Is.EqualTo(Hidden));
			Assert.That(redactor.Redact(""), Is.EqualTo(Hidden));
			Assert.That(redactor.Redact(null, new RedactorOptions { MaskToken = "***" }), Is.EqualTo("***"));
		});
	}

	[TestCaseSource(nameof(Redactors))]
	public void Should_hand_null_and_empty_back_when_masking(IRedactor redactor)
	{
		var options = new RedactorOptions { Mode = RedactionMode.Mask };
		Assert.Multiple(() =>
		{
			Assert.That(redactor.Redact(null, options), Is.Null);
			Assert.That(redactor.Redact("", options), Is.EqualTo(""));
		});
	}

	[TestCaseSource(nameof(Redactors))]
	public void Should_accept_the_key_shaped_call(IRedactor redactor)
	{
		// The overload existing callers were written against; the key rides along as a hint.
		Assert.Multiple(() =>
		{
			Assert.That(redactor.Redact("plain words only", "Some:Setting"), Is.EqualTo("plain words only"));
			Assert.That(redactor.Redact("plain words only"), Is.EqualTo("plain words only"));
			Assert.That(redactor.Redact("plain words only", (string?)null), Is.EqualTo("plain words only"));
		});
	}

	[TestCaseSource(nameof(Redactors))]
	public void Should_leave_a_secret_free_line_untouched(IRedactor redactor)
	{
		const string line = "Ledger synced 12 transactions for BTC_USDT in 340 ms";
		Assert.That(redactor.Redact(line), Is.EqualTo(line));
	}

	[TestCaseSource(nameof(Redactors))]
	public void Should_hide_a_uri_password_by_default(IRedactor redactor)
	{
		var redacted = redactor.Redact("mongodb://bob:8dfaec50e181fedcba@localhost/db");
		Assert.Multiple(() =>
		{
			Assert.That(redacted, Does.Not.Contain("8dfaec50e181fedcba"));
			Assert.That(redacted, Does.Contain("@localhost/db"));
		});
	}

	[Test]
	public void Should_read_a_credential_shaped_key_case_insensitively()
	{
		Assert.Multiple(() =>
		{
			Assert.That(new RedactorOptions { Key = "Poloniex:ApiKey" }.KeyLooksSecret, Is.True);
			Assert.That(new RedactorOptions { Key = "CLIENT_SECRET" }.KeyLooksSecret, Is.True);
			Assert.That(new RedactorOptions { Key = "ConnectionStrings:Default" }.KeyLooksSecret, Is.True);
			Assert.That(new RedactorOptions { Key = "Logging:LogLevel:Default" }.KeyLooksSecret, Is.False);
			Assert.That(new RedactorOptions { Key = null }.KeyLooksSecret, Is.False);
			Assert.That(new RedactorOptions { Key = "" }.KeyLooksSecret, Is.False);
		});
	}
}
