using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace XKit.Redactor;

/// <summary>
/// Finds secrets in text whose shape is unknown by asking how random each piece of it looks.
///
/// <para>
/// Every alphanumeric token is split on case transitions, the pieces found in the
/// <see cref="WordDictionary"/> are dropped, dictionary words that ran together in a single case are
/// stripped from what is left, and the Shannon entropy of the remainder decides. Above the threshold
/// the whole token is hidden. A credential-shaped <see cref="RedactorOptions.Key"/> lowers the
/// threshold rather than deciding outright. On top of that: GUIDs are always hidden, the password
/// in an absolute URI is always hidden, a hex-looking token is measured as bytes rather than
/// characters, and a value that is mostly hidden and base64-shaped collapses to a single token.
/// </para>
///
/// <para>
/// Cheap enough for a log sink: per token it does one dictionary pass, not one
/// <c>string.Replace</c> per dictionary word. Thread-safe; all state per call is local.
/// </para>
/// </summary>
public class EntropyRedactor : IRedactor
{
	private const double DefaultThreshold = 3.3;

	/// <summary>
	/// The bar a value found under a credential-shaped key has to clear. Lower, not zero: the key
	/// name is a hint, and a "password" that is "changeme" still reads as a word.
	/// </summary>
	private const double SecretKeyThreshold = 2.9;

	private const int StackBufferLength = 256;

	private readonly Regex _base64Rx = new(@"^(?<![A-Za-z0-9+/=])([A-Za-z0-9+/]{12,}={0,2})(?![A-Za-z0-9+/=])$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
	private readonly Regex _guidRx = new(@"\b[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
	private readonly Regex _hexRx = new(@"^[0-9a-fA-F]{6,}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private readonly WordDictionary _dictionary;

	/// <summary>
	/// Builds its own <see cref="WordDictionary"/>, which reads the embedded word list. Fine for a
	/// singleton; for anything created per call, share a dictionary through the other constructor.
	/// </summary>
	public EntropyRedactor()
		: this(null)
	{
	}

	/// <param name="dictionary">The words to ignore; a fresh <see cref="WordDictionary"/> when null.</param>
	public EntropyRedactor(WordDictionary? dictionary)
	{
		_dictionary = dictionary ?? new WordDictionary();
	}

	/// <inheritdoc />
	[return: NotNullIfNotNull(nameof(value))]
	public string? Redact(string? value, RedactorOptions? options = null)
	{
		options ??= new RedactorOptions();

		if (string.IsNullOrEmpty(value))
		{
			return options.Mode == RedactionMode.Mask ? value : options.Hide(string.Empty, isolated: false);
		}

		// A GUID anywhere is hidden before anything else looks at the text.
		value = _guidRx.Replace(value, match => Hide(match.Value, options));

		// The password in an absolute URI is hidden whatever it looks like.
		if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
		{
			var builder = new UriBuilder(uri);
			builder.Password = Hide(builder.Password, options);
			value = builder.ToString();
		}

		var threshold = options.KeyLooksSecret ? SecretKeyThreshold : DefaultThreshold;

		var result = new StringBuilder(value.Length);
		var entropySource = new StringBuilder();
		var hiddenLength = 0;
		var longWordsFound = 0;

		var tokens = new AlphanumericTokenizer(value.AsSpan());
		while (tokens.MoveNext(out var token))
		{
			if (!char.IsLetterOrDigit(token[0]))
			{
				result.Append(token);
				continue;
			}

			// Only the pieces the dictionary does not know contribute to the entropy.
			entropySource.Clear();
			var words = new WordTokenizer(token);
			while (words.MoveNext(out var word))
			{
				if (word.Length <= 2 || !_dictionary.IsKnownWord(word))
				{
					entropySource.Append(word);
				}
				else if (word.Length > 3)
				{
					longWordsFound++;
				}
			}

			var source = entropySource.ToString();
			var entropy = EntropyOfUnknownPart(source);
			if (source.Length % 2 == 0 && _hexRx.IsMatch(source))
			{
				// Hex-looking: measure the bytes it encodes, which is the alphabet that actually
				// matters. Not enough on its own to hide it - plenty of words are valid hex.
				var hexEntropy = ShannonEntropy<byte>(Convert.FromHexString(source));
				if (hexEntropy > entropy)
				{
					entropy = hexEntropy;
				}

				if (source.Length is 4 or 8 or 16 or 32 or 64)
				{
					// Common lengths of hex-encoded ids, hashes and keys.
					entropy += 0.5;
				}
			}

			if (entropy >= threshold)
			{
				result.Append(Hide(token.ToString(), options));
				hiddenLength += token.Length;
			}
			else
			{
				result.Append(token);
			}
		}

		// Pure base64 with most of it hidden and no real words in it: one token, not a patchwork.
		if (hiddenLength * 1.0 / value.Length > 0.5 && longWordsFound < 2 && _base64Rx.IsMatch(value))
		{
			return Hide(value, options);
		}

		return result.ToString();
	}

	/// <summary>
	/// Every span this detector hides is the secret and nothing else - a token, a GUID, a URI
	/// password, a whole base64 value - so masking is safe in every case.
	/// </summary>
	private string Hide(string secret, RedactorOptions options)
	{
		return options.Hide(secret, isolated: true);
	}

	private double EntropyOfUnknownPart(string source)
	{
		Span<char> buffer = source.Length <= StackBufferLength
			? stackalloc char[source.Length]
			: new char[source.Length];
		var length = _dictionary.StripKnownWords(source.AsSpan(), buffer);
		return ShannonEntropy<char>(buffer.Slice(0, length));
	}

	private double ShannonEntropy<T>(ReadOnlySpan<T> values)
		where T : struct
	{
		if (values.Length == 0)
		{
			return 0;
		}

		var frequencies = new Dictionary<T, int>();
		foreach (var value in values)
		{
			ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(frequencies, value, out _);
			count++;
		}

		double entropy = 0;
		foreach (var count in frequencies.Values)
		{
			var probability = (double)count / values.Length;
			entropy -= probability * Math.Log2(probability);
		}

		return entropy;
	}

	/// <summary>
	/// Splits text into runs of letters-or-digits and single non-alphanumeric characters. Case is
	/// not a boundary here: "VisualStudio" is one token, so a hidden token is a whole identifier.
	/// </summary>
	private ref struct AlphanumericTokenizer
	{
		private readonly ReadOnlySpan<char> _input;
		private int _position;

		public AlphanumericTokenizer(ReadOnlySpan<char> input)
		{
			_input = input;
			_position = 0;
		}

		public bool MoveNext(out ReadOnlySpan<char> token)
		{
			if (_position >= _input.Length)
			{
				token = default;
				return false;
			}

			var start = _position;
			_position++;
			if (char.IsLetterOrDigit(_input[start]))
			{
				while (_position < _input.Length && char.IsLetterOrDigit(_input[_position]))
				{
					_position++;
				}
			}

			token = _input.Slice(start, _position - start);
			return true;
		}
	}

	/// <summary>
	/// Splits a token into words: runs of letters, broken where a lower-case letter meets an
	/// upper-case one, so "clientSecret" is "client" and "Secret". Digits and anything else come
	/// out one character at a time.
	/// </summary>
	private ref struct WordTokenizer
	{
		private readonly ReadOnlySpan<char> _input;
		private int _position;

		public WordTokenizer(ReadOnlySpan<char> input)
		{
			_input = input;
			_position = 0;
		}

		public bool MoveNext(out ReadOnlySpan<char> token)
		{
			if (_position >= _input.Length)
			{
				token = default;
				return false;
			}

			var start = _position;
			_position++;
			if (char.IsLetter(_input[start]))
			{
				while (_position < _input.Length)
				{
					var previous = _input[_position - 1];
					var current = _input[_position];
					if (!char.IsLetter(current) || (char.IsLower(previous) && char.IsUpper(current)))
					{
						break;
					}

					_position++;
				}
			}

			token = _input.Slice(start, _position - start);
			return true;
		}
	}
}
