namespace XKit.Redactor;

/// <summary>
/// An exception whose message is redacted on construction, and whose whole chain is redacted again
/// whenever it is formatted. Use it where a failure has to be reported but the text describing it
/// would quote a credential - a connection string that will not parse is the case it was written
/// for.
///
/// <para>
/// <b>Hiding a secret is a legitimate reason to wrap an exception. It is never a reason to drop
/// one.</b> That is why there is no constructor that omits the inner exception: the rule that the
/// original is always chained is a compile-time guarantee here rather than something to catch in
/// review. A copy of an exception's text is not the exception - the stack trace is the valuable
/// part, and it only exists on the original object.
/// </para>
///
/// <para>
/// <b>Throw it from the validation site, not from a <c>ToString()</c>.</b> A describer is usually
/// formatted by the very log line trying to report the problem, so throwing from one breaks the
/// reporting rather than improving it. Validate where the value enters; let the describer fall back
/// to the mask token, since by then the validation site has already failed loudly.
/// </para>
///
/// <para>
/// <b>Check your library before reaching for this.</b> A well-behaved one already redacts its own
/// messages - MongoDB.Driver reports a bad connection string as
/// <c>mongodb://&lt;hidden&gt;@host/db</c> - and where that holds, a plain wrapper with the redacted
/// value in its own message is enough. This type is for the libraries that misbehave, and for the
/// call sites that would rather not have to know which kind they are talking to. Either way, write
/// a test that fails if a library's redaction ever stops.
/// </para>
/// </summary>
/// <example>
/// <code>
/// catch (MongoConfigurationException ex)
/// {
/// 	throw new RedactedException($"ConnectionStrings:Default is not a valid connection string ({connectionString})", ex);
/// }
/// </code>
/// </example>
public class RedactedException : Exception
{
	private readonly IRedactor _redactor;
	private readonly RedactorOptions? _options;

	/// <param name="message">
	/// The message, with the secret still in it - it is redacted here, so a call site cannot forget
	/// to. A null message follows the mode like any other value: erased to the mask token by default.
	/// </param>
	/// <param name="innerException">
	/// The original failure. Required, and deliberately so.
	/// </param>
	/// <param name="redactor">
	/// How to find the secrets. <see cref="CredentialRedactor.Default"/> when null, which is the
	/// right answer for a call site that has no redactor to hand; pass the injected one where there
	/// is one, and the entropy redactor where the text's shape is unknown.
	/// </param>
	/// <param name="options">
	/// Mode, key and mask token, as anywhere else. Held for the lifetime of the exception and used
	/// again by <see cref="ToString"/>.
	/// </param>
	public RedactedException(string? message, Exception innerException, IRedactor? redactor = null, RedactorOptions? options = null)
		: base(
			(redactor ?? CredentialRedactor.Default).Redact(message, options)
			, innerException ?? throw new ArgumentNullException(nameof(innerException), "A redacted exception still has to carry the original: the stack trace is the part worth keeping.")
		)
	{
		_redactor = redactor ?? CredentialRedactor.Default;
		_options = options;
	}

	/// <summary>
	/// The formatted chain, run through the redactor - so a secret quoted by the <i>inner</i>
	/// exception's own message does not reach a log sink that formats this one.
	///
	/// <para>
	/// Sealed, and not optional. The whole value of the type is that it cannot leak, and a switch to
	/// turn that off is the switch someone flips while chasing a number in a log loop. The cost is
	/// one redactor pass over text that already cost a stack-trace materialisation to produce, which
	/// is the expensive half of formatting an exception by a wide margin.
	/// </para>
	/// </summary>
	public sealed override string ToString()
	{
		return _redactor.Redact(base.ToString(), _options);
	}
}
