using System.Globalization;

namespace XKit.Redactor;

/// <summary>
/// The words the entropy detector ignores: 10,000 common English words, a short developer
/// vocabulary, the current user and machine names, recent years, and whatever the host adds. A
/// token made of these is prose, however random its letters look.
///
/// <para>
/// Built once, in the constructor, from the embedded resources. Share one instance - through DI as
/// a singleton, or by handing it to every <see cref="EntropyRedactor"/> - rather than building one
/// per call.
/// </para>
/// </summary>
public class WordDictionary
{
	/// <summary>
	/// Words shorter than this are never stripped out of a token: "an" and "is" occur inside random
	/// text far too often to mean anything.
	/// </summary>
	public const int MinimumStrippableLength = 3;

	private const string EnglishResource = "XKit.Redactor.Implementation.Resources.google-10000-english.txt";
	private const string DeveloperishResource = "XKit.Redactor.Implementation.Resources.developerish.txt";

	/// <summary>
	/// Sorted case-insensitively so a span can be looked up by binary search without turning it into
	/// a string first. That is what keeps the detector allocation-light per token.
	/// </summary>
	private readonly string[] _sorted;

	private readonly int _maxWordLength;

	/// <summary>
	/// The embedded word list alone.
	/// </summary>
	public WordDictionary()
		: this(null)
	{
	}

	/// <param name="additionalWords">
	/// Product names, host names, anything else that would otherwise read as random - "quotaly",
	/// "poloniex". Case does not matter.
	/// </param>
	public WordDictionary(IEnumerable<string>? additionalWords)
	{
		var words = new HashSet<string>(11_000, StringComparer.OrdinalIgnoreCase);

		ReadEmbedded(words, EnglishResource);
		ReadEmbedded(words, DeveloperishResource);

		AddIfNotEmpty(words, Environment.UserName);
		AddIfNotEmpty(words, Environment.MachineName);

		// Years turn up in paths and version strings; dropping them keeps a date from looking random.
		for (int year = 2000, last = DateTime.UtcNow.Year + 5; year < last; year++)
		{
			words.Add(year.ToString(CultureInfo.InvariantCulture));
		}

		if (additionalWords is not null)
		{
			foreach (var word in additionalWords)
			{
				AddIfNotEmpty(words, word);
			}
		}

		_sorted = words.ToArray();
		Array.Sort(_sorted, StringComparer.OrdinalIgnoreCase);
		_maxWordLength = _sorted.Max(w => w.Length);
	}

	/// <summary>
	/// How many distinct words are known.
	/// </summary>
	public int Count => _sorted.Length;

	/// <summary>
	/// Whether <paramref name="word"/> is in the dictionary, ignoring case.
	/// </summary>
	public bool Contains(ReadOnlySpan<char> word)
	{
		return IndexOf(word) >= 0;
	}

	/// <summary>
	/// <see cref="Contains"/>, plus the plural: "tokens" counts when "token" is known.
	/// </summary>
	public bool IsKnownWord(ReadOnlySpan<char> word)
	{
		if (Contains(word))
		{
			return true;
		}

		return word.Length > 1
			&& (word[word.Length - 1] == 's' || word[word.Length - 1] == 'S')
			&& Contains(word.Slice(0, word.Length - 1));
	}

	/// <summary>
	/// Copies <paramref name="source"/> to <paramref name="destination"/> with every dictionary word
	/// of <see cref="MinimumStrippableLength"/> or more removed, in one left-to-right pass taking the
	/// longest match at each position. Returns how many characters were written.
	///
	/// <para>
	/// This is the second look at a token: the first split it on case transitions and dropped the
	/// words it recognised, and this catches the ones that ran together in a single case -
	/// "thepassword123" leaves "123". It replaces what used to be one <c>string.Replace</c> per
	/// dictionary word per token, which was ten thousand allocations a call.
	/// </para>
	/// </summary>
	public int StripKnownWords(ReadOnlySpan<char> source, Span<char> destination)
	{
		var written = 0;
		var position = 0;
		while (position < source.Length)
		{
			var matched = LongestKnownWordAt(source, position);
			if (matched > 0)
			{
				position += matched;
				continue;
			}

			destination[written++] = source[position++];
		}

		return written;
	}

	private int LongestKnownWordAt(ReadOnlySpan<char> source, int position)
	{
		var longest = Math.Min(_maxWordLength, source.Length - position);
		for (var length = longest; length >= MinimumStrippableLength; length--)
		{
			if (Contains(source.Slice(position, length)))
			{
				return length;
			}
		}

		return 0;
	}

	private int IndexOf(ReadOnlySpan<char> word)
	{
		var low = 0;
		var high = _sorted.Length - 1;
		while (low <= high)
		{
			var middle = low + ((high - low) >> 1);
			var comparison = _sorted[middle].AsSpan().CompareTo(word, StringComparison.OrdinalIgnoreCase);
			if (comparison == 0)
			{
				return middle;
			}

			if (comparison < 0)
			{
				low = middle + 1;
			}
			else
			{
				high = middle - 1;
			}
		}

		return -1;
	}

	private void ReadEmbedded(HashSet<string> words, string resourceName)
	{
		var assembly = typeof(WordDictionary).Assembly;
		using var stream = assembly.GetManifestResourceStream(resourceName)
			?? throw new InvalidOperationException($"Missing embedded resource '{resourceName}'. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
		using var reader = new StreamReader(stream);
		while (reader.ReadLine() is { } line)
		{
			AddIfNotEmpty(words, line);
		}
	}

	private void AddIfNotEmpty(HashSet<string> words, string? word)
	{
		if (!string.IsNullOrWhiteSpace(word))
		{
			words.Add(word.Trim());
		}
	}
}
