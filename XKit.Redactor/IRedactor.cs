using System.Diagnostics.CodeAnalysis;

namespace XKit.Redactor;

/// <summary>
/// Takes secrets out of text before it goes somewhere it cannot be recalled from - a log file, a
/// notification, an admin page. The text may be a bare value or a whole log line; the redactor finds
/// what it can and leaves the rest readable.
///
/// <para>
/// Null and empty input follow the mode. <see cref="RedactionMode.Erase"/> answers with the mask
/// token no matter what, so a log line can never be read to mean "this one is not configured".
/// <see cref="RedactionMode.Mask"/> hands null and empty back unchanged - that mode already reveals
/// something about every secret, so whether there was one is fair to show too.
/// </para>
/// </summary>
public interface IRedactor
{
	/// <summary>
	/// Redacts <paramref name="value"/> according to <paramref name="options"/>. With no options,
	/// every secret found is erased and the default mask token is used.
	/// </summary>
	[return: NotNullIfNotNull(nameof(value))]
	string? Redact(string? value, RedactorOptions? options = null);
}
