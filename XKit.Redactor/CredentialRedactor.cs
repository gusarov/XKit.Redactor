using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace XKit.Redactor;

/// <summary>
/// Strips credentials out of text by their shape: a URL with user info, a <c>password=</c> pair, a
/// credential-named key in a JSON document. Cheap enough to sit inside a log sink, so a connection
/// string, a signed URL or an appsettings file quoted into an exception cannot reach a log file no
/// matter which call site formatted it.
///
/// <para>
/// Two things happen here and they are deliberately separate. The rules <i>find</i> credentials in
/// text whose shape is otherwise unknown; <see cref="RedactionMode"/> decides what each one
/// <i>becomes</i>. The mode is applied to every credential found, independently, so text carrying
/// two secrets does not have to trade one against the other.
/// </para>
///
/// <para>
/// When no rule fires and <see cref="RedactorOptions.Key"/> reads as a credential name, the whole
/// value is treated as the secret. The rules run first so that a connection string under a key like
/// "ConnectionString" keeps its host and database - the more useful answer - and only falls back to
/// hiding everything when the structure gave nothing to hold on to.
/// </para>
/// </summary>
public class CredentialRedactor : IRedactor
{
	/// <summary>
	/// One way of finding a credential. <see cref="Pattern"/> matches the surrounding shape and
	/// captures the part to replace as <c>secret</c>, so the structure that identifies it - the
	/// scheme, the key name, the quotes - survives and stays readable.
	/// </summary>
	private sealed class Rule
	{
		public Rule(Regex pattern, bool isolatesTheSecret)
		{
			Pattern = pattern;
			IsolatesTheSecret = isolatesTheSecret;
		}

		public Regex Pattern { get; }

		/// <summary>
		/// Whether the captured span is the secret and nothing else. Only then may
		/// <see cref="RedactionMode.Mask"/> apply: masking reveals both ends of whatever it is given,
		/// so on a span that merely <i>contains</i> a secret the revealed end can be the secret
		/// itself. A rule capturing a composite span erases in every mode.
		/// </summary>
		public bool IsolatesTheSecret { get; }
	}

	/// <summary>
	/// A shared instance for callers with nowhere to keep one of their own - notably
	/// <see cref="RedactorException"/> when it is handed no redactor. Constructing a redactor
	/// compiles three regexes, which is far too much work to repeat per exception, and the type
	/// holds no per-call state, so one instance is safe to share across threads.
	///
	/// <para>
	/// Anything with a composition root should register an <see cref="IRedactor"/> there instead and
	/// let it be injected; this exists for the places that cannot.
	/// </para>
	/// </summary>
	public static CredentialRedactor Default { get; } = new CredentialRedactor();

	private readonly Rule[] _rules =
	[
		// scheme://user:password@host -> scheme://***@host. The user name goes as well: host, database
		// and application name are all a log needs to identify a connection. The captured span is the
		// whole "user:password" pair, which is composite - its tail is the password - so it never
		// masks.
		new Rule(
			new Regex(@"\b[a-zA-Z][a-zA-Z0-9+.\-]*://(?<secret>[^/@\s]+)@", RegexOptions.Compiled | RegexOptions.CultureInvariant)
			, isolatesTheSecret: false
		),

		// password=secret / pwd=secret in key-value connection strings and query strings.
		new Rule(
			new Regex(@"\b(?:password|pwd)\s*=\s*(?<secret>[^;&\s""']+)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
			, isolatesTheSecret: true
		),

		// "apiKey": "secret" in a JSON document - an appsettings file quoted into an error, a request
		// body echoed back by a driver. The key name is the only evidence available that the value is
		// a credential, so this is a heuristic, and it is written to over-match: a harmless value
		// hidden costs a debugging session, a live key printed costs a rotation. "connectionString" is
		// deliberately not in the list - the first rule already takes the password out of one and
		// leaves the host and database, which is the more useful answer.
		new Rule(
			new Regex(@"""[^""\\]*(?:password|pwd|secret|token|apikey|api_key)[^""\\]*""\s*:\s*""(?<secret>[^""\\]*)""", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
			, isolatesTheSecret: true
		),
	];

	/// <inheritdoc />
	[return: NotNullIfNotNull(nameof(value))]
	public string? Redact(string? value, RedactorOptions? options = null)
	{
		options ??= new RedactorOptions();

		if (string.IsNullOrEmpty(value))
		{
			return options.Mode == RedactionMode.Mask ? value : options.Hide(string.Empty, isolated: false);
		}

		var found = false;
		var redacted = value!;
		foreach (var rule in _rules)
		{
			redacted = rule.Pattern.Replace(redacted, match =>
			{
				found = true;
				return Splice(match, rule, options);
			});
		}

		if (!found && options.KeyLooksSecret)
		{
			return options.Hide(value!, isolated: true);
		}

		return redacted;
	}

	/// <summary>
	/// Splices the replacement in where the captured secret stood, so whatever the rule matched
	/// around it is preserved verbatim.
	/// </summary>
	private string Splice(Match match, Rule rule, RedactorOptions options)
	{
		var secret = match.Groups["secret"];
		var replacement = options.Hide(secret.Value, rule.IsolatesTheSecret);

		var offset = secret.Index - match.Index;
		return match.Value.Substring(0, offset) + replacement + match.Value.Substring(offset + secret.Length);
	}
}
