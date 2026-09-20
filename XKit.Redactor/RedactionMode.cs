namespace XKit.Redactor;

/// <summary>
/// What a secret a redactor has found is turned into. Finding a secret and deciding how much of it
/// may survive are separate questions, and only the caller knows the answer to the second one.
///
/// <para>
/// <see cref="Erase"/> and <see cref="Mask"/> trade off how much of the <i>value</i> survives.
/// <see cref="Label"/> is a different axis: nothing of the value survives, and something about its
/// <i>context</i> is said instead.
/// </para>
/// </summary>
public enum RedactionMode
{
	/// <summary>
	/// Nothing of the secret survives - not a character, not its length, not whether it was null. The
	/// default, and the only safe answer when the text is going somewhere it cannot be recalled from.
	/// </summary>
	Erase,

	/// <summary>
	/// Each secret is replaced by its <see cref="SecretMaskingExtensions.Mask"/>, so two different
	/// secrets in the same text stay tellable apart - enough to answer "is this still the key I
	/// rotated?" without publishing either. It costs up to three characters per end of every secret
	/// it touches, so it is opt-in and never the default. Null and empty come back unchanged in this
	/// mode.
	/// </summary>
	Mask,

	/// <summary>
	/// Nothing of the secret survives, and a label saying <i>what it was</i>, or <i>why it is
	/// hidden</i>, is wrapped in the mask token instead: <c>●●●apikey●●●</c>,
	/// <c>mongodb://●●●userinfo●●●@host/db</c>, <c>●●●MongoConfigurationException●●●</c>.
	///
	/// <para>
	/// It exists because "hidden because secret" and "hidden because broken" otherwise look identical
	/// to whoever reads the log - different faults needing different fixes, reported the same way.
	/// </para>
	///
	/// <para>
	/// <b>The label must be something the code chose</b> - a key name, a rule name, a type name -
	/// <b>never anything read out of the value.</b> An exception's <c>GetType().Name</c> is safe; its
	/// <c>Message</c> is not, and neither is the value's length, its first characters, or whether it
	/// was null. See <see cref="RedactorOptions.Label"/> for where the label comes from.
	/// </para>
	///
	/// <para>
	/// This does disclose the <i>kind</i> of secret - <c>●●●apikey●●●</c> says an API key was there.
	/// That is the point of the mode rather than a side effect, and it is usually obvious from
	/// context anyway, but it is a decision on the record. It is mutually exclusive with
	/// <see cref="Mask"/>: a label and a partial reveal in one output is nothing anybody wants.
	/// </para>
	/// </summary>
	Label,
}
