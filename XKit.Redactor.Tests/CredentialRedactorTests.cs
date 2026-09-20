namespace XKit.Redactor.Tests;

public class CredentialRedactorTests
{
	private const string Hidden = RedactorOptions.DefaultMaskToken;

	private readonly CredentialRedactor _redactor = new();

	[TestCase(
		"MongoDBStorage connected to MongoDB mongodb+srv://user:p%40ssw0rd@mongo.xkit.tools/xkit_apextroid?appName=apex 12",
		$"MongoDBStorage connected to MongoDB mongodb+srv://{Hidden}@mongo.xkit.tools/xkit_apextroid?appName=apex 12")]
	[TestCase(
		"mongodb://root:example@mongo:27017/db",
		$"mongodb://{Hidden}@mongo:27017/db")]
	[TestCase(
		"wss://ws.poloniex.com/ws/public",
		"wss://ws.poloniex.com/ws/public")]
	[TestCase(
		"Server=db;User Id=sa;Password=Sup3r;Encrypt=true",
		$"Server=db;User Id=sa;Password={Hidden};Encrypt=true")]
	[TestCase(
		"https://host/api?pwd=abc&x=1",
		$"https://host/api?pwd={Hidden}&x=1")]
	public void Should_hide_credentials_and_leave_everything_else(string input, string expected)
	{
		Assert.That(_redactor.Redact(input), Is.EqualTo(expected));
	}

	[TestCase(
		"{ \"Poloniex\": { \"ApiKey\": \"abcdefghijklmnop\", \"SecretKey\": \"0123456789abcdef\" } }",
		$"{{ \"Poloniex\": {{ \"ApiKey\": \"{Hidden}\", \"SecretKey\": \"{Hidden}\" }} }}")]
	[TestCase(
		"{ \"BotToken\": \"12345:AAHfLongTelegramSecret\" }",
		$"{{ \"BotToken\": \"{Hidden}\" }}")]
	[TestCase(
		"{ \"Pair\": \"BTC_USDT\", \"Amount\": \"100\" }",
		"{ \"Pair\": \"BTC_USDT\", \"Amount\": \"100\" }")]
	public void Should_hide_credential_shaped_values_in_json(string input, string expected)
	{
		// An appsettings file quoted into an exception message is the realistic case. The key name is
		// the only evidence there is, so the rule over-matches on purpose.
		Assert.That(_redactor.Redact(input), Is.EqualTo(expected));
	}

	[Test]
	public void Should_leave_a_connection_string_value_in_json_readable_apart_from_its_password()
	{
		// "ConnectionString" is not a credential-shaped key, so the URL rule handles the value and the
		// host and database survive - more useful than hiding the lot.
		var input = "{ \"ConnectionString\": \"mongodb://apex:Sup3rS3cret@mongo.xkit.tools/xkit_apextroid\" }";
		Assert.That(_redactor.Redact(input), Is.EqualTo($"{{ \"ConnectionString\": \"mongodb://{Hidden}@mongo.xkit.tools/xkit_apextroid\" }}"));
	}

	[Test]
	public void Should_apply_the_mode_to_each_secret_independently()
	{
		// Two credentials in one line, each replaced on its own. Masking makes them tellable apart,
		// which is the whole reason the mode exists.
		var input = "pwd=FirstSecretValue and pwd=SecondSecretValue";
		var options = new RedactorOptions { Mode = RedactionMode.Mask };
		Assert.That(_redactor.Redact(input, options), Is.EqualTo($"pwd=Fir{Hidden}lue and pwd=Sec{Hidden}lue"));
	}

	[Test]
	public void Should_erase_by_default_even_where_masking_is_allowed()
	{
		Assert.That(_redactor.Redact("pwd=FirstSecretValue"), Is.EqualTo($"pwd={Hidden}"));
	}

	[Test]
	public void Should_never_mask_a_user_info_pair_because_its_tail_is_the_password()
	{
		// The captured span is "apex:Sup3rS3cret" - composite, so masking it would reveal "ret". This
		// rule erases whatever the mode says, which is the lesson that cost a commit to learn.
		var options = new RedactorOptions { Mode = RedactionMode.Mask };
		var redacted = _redactor.Redact("mongodb://apex:Sup3rS3cret@host/db", options);
		Assert.Multiple(() =>
		{
			Assert.That(redacted, Is.EqualTo($"mongodb://{Hidden}@host/db"));
			Assert.That(redacted, Does.Not.Contain("ret"));
		});
	}

	[Test]
	public void Should_still_hide_a_short_secret_entirely_when_masking()
	{
		// Mask() reveals nothing below nine characters, so the mode cannot leak a short password.
		var options = new RedactorOptions { Mode = RedactionMode.Mask };
		Assert.That(_redactor.Redact("pwd=Sup3r", options), Is.EqualTo($"pwd={Hidden}"));
	}

	[Test]
	public void Should_hide_the_whole_value_when_only_the_key_says_it_is_a_secret()
	{
		// No structure to hold on to - not a URL, not a pair, not JSON - so the key name is the
		// only evidence, and it is enough.
		Assert.Multiple(() =>
		{
			Assert.That(_redactor.Redact("abcdefghijklmnop", "Poloniex:ApiKey"), Is.EqualTo(Hidden));
			Assert.That(_redactor.Redact("abcdefghijklmnop", "Poloniex:Pair"), Is.EqualTo("abcdefghijklmnop"));
			Assert.That(_redactor.Redact("abcdefghijklmnop"), Is.EqualTo("abcdefghijklmnop"));
		});
	}

	[Test]
	public void Should_prefer_the_structural_rule_over_the_key_hint()
	{
		// The key says "ConnectionString", but the URL rule already took the password out and left
		// the host, which is what a reader needs. Hiding the lot on the key's say-so would be worse.
		var redacted = _redactor.Redact("mongodb://apex:Sup3rS3cret@host/db", "ConnectionStrings:Default");
		Assert.That(redacted, Is.EqualTo($"mongodb://{Hidden}@host/db"));
	}

	[Test]
	public void Should_mask_a_whole_value_hidden_on_the_keys_say_so()
	{
		// The whole value is the secret, so it is isolated and the mode applies.
		var options = new RedactorOptions { Key = "SecretKey", Mode = RedactionMode.Mask };
		Assert.That(_redactor.Redact("0123456789abcdef", options), Is.EqualTo($"012{Hidden}def"));
	}

	[Test]
	public void Should_use_the_callers_mask_token()
	{
		var options = new RedactorOptions { MaskToken = "***" };
		Assert.That(_redactor.Redact("pwd=FirstSecretValue", options), Is.EqualTo("pwd=***"));
	}
}
