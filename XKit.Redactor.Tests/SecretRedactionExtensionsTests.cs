namespace XKit.Redactor.Tests;

/// <summary>
/// Redaction of text we did not write - driver messages, third party errors - by a secret whose
/// value is known.
/// </summary>
public class SecretRedactionExtensionsTests
{
	private const string Hidden = RedactorOptions.DefaultMaskToken;
	private const string ConnectionString = "mongodb://admin:s3cr3t-p4ssw0rd@db.internal:27017/mail";

	private static readonly RedactorOptions _mask = new() { Mode = RedactionMode.Mask };

	[Test]
	public void Should_erase_the_whole_secret_where_it_is_quoted_back()
	{
		var message = $"The connection string '{ConnectionString}' is not valid.";

		var redacted = message.Redact(ConnectionString);

		Assert.Multiple(() =>
		{
			Assert.That(redacted, Is.EqualTo($"The connection string '{Hidden}' is not valid."));
			Assert.That(redacted, Does.Not.Contain("s3cr3t-p4ssw0rd"));
		});
	}

	[Test]
	public void Should_erase_the_password_on_its_own()
	{
		// Only the credential leaked, not the whole connection string.
		var message = "Authentication failed for user 'admin' with password 's3cr3t-p4ssw0rd'.";

		var redacted = message.Redact(ConnectionString);

		Assert.Multiple(() =>
		{
			Assert.That(redacted, Is.EqualTo($"Authentication failed for user 'admin' with password '{Hidden}'."));
			Assert.That(redacted, Does.Contain("user 'admin'"), "everything that is not the secret survives");
		});
	}

	[Test]
	public void Should_mask_the_password_but_never_the_url_when_asked()
	{
		// The password is isolated, so it may be masked. The connection string is composite - its
		// tail is the host and database - so it is erased even in this mode.
		var message = $"'{ConnectionString}' failed: password 's3cr3t-p4ssw0rd' rejected";

		var redacted = message.Redact(ConnectionString, _mask);

		Assert.That(redacted, Is.EqualTo($"'{Hidden}' failed: password 's3c{Hidden}0rd' rejected"));
	}

	[Test]
	public void Should_mask_an_opaque_secret_when_asked()
	{
		var redacted = "key 0123456789abcdef rejected".Redact("0123456789abcdef", _mask);
		Assert.That(redacted, Is.EqualTo($"key 012{Hidden}def rejected"));
	}

	[Test]
	public void Should_leave_text_without_the_secret_alone()
	{
		const string message = "connectTimeoutMS has an invalid TimeSpan value of abc.";

		Assert.That(message.Redact(ConnectionString), Is.EqualTo(message));
	}

	[Test]
	public void Should_not_garble_text_for_a_very_short_password()
	{
		// "pw" would otherwise be replaced inside unrelated words across the whole stack trace.
		const string shortPassword = "mongodb://admin:pw@db.internal/mail";
		const string message = "at Company.Framework.Pwd.Parse() - powered by pw";

		Assert.Multiple(() =>
		{
			Assert.That(message.Redact(shortPassword), Is.EqualTo(message), "a 2 character password is not distinctive enough to replace standalone");
			Assert.That(shortPassword.Redact(shortPassword), Is.EqualTo(Hidden), "but the connection string itself is always hidden");
		});
	}

	[Test]
	public void Should_follow_the_mode_for_missing_text()
	{
		Assert.Multiple(() =>
		{
			Assert.That(((string?)null).Redact(ConnectionString), Is.EqualTo(Hidden), "erase never says whether there was anything");
			Assert.That("".Redact(ConnectionString), Is.EqualTo(Hidden));
			Assert.That(((string?)null).Redact(ConnectionString, _mask), Is.Null, "mask does");
			Assert.That("".Redact(ConnectionString, _mask), Is.EqualTo(""));
		});
	}

	[Test]
	public void Should_leave_text_alone_when_there_is_no_secret_to_find()
	{
		Assert.Multiple(() =>
		{
			Assert.That("anything".Redact(null), Is.EqualTo("anything"));
			Assert.That("anything".Redact(""), Is.EqualTo("anything"));
		});
	}

	[Test]
	public void Should_cope_with_a_connection_string_that_has_no_credentials()
	{
		const string noCredentials = "mongodb://db.internal:27017/mail";
		const string message = "The connection string 'mongodb://db.internal:27017/mail' is not valid.";

		Assert.That(message.Redact(noCredentials), Is.EqualTo($"The connection string '{Hidden}' is not valid."));
	}
}
