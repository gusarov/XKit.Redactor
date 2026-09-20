# XKit.Redactor

One contract for taking secrets out of text before it goes somewhere it cannot be recalled from - a log file, a notification, an admin page - and two ways of finding them.

| Package | Targets | What is in it |
| --- | --- | --- |
| `XKit.Redactor` | netstandard2.0, netstandard2.1, net8.0, net9.0, net10.0 | `IRedactor`, `RedactorOptions`, `RedactionMode`, the rule-based `CredentialRedactor`, `Mask()` and `Redact(secret)` string helpers |
| `XKit.Redactor.Implementation` | net8.0, net9.0, net10.0 | `EntropyRedactor` - finds secrets by how random they look, with an embedded English + developer word list |

## The contract

```csharp
public interface IRedactor
{
	[return: NotNullIfNotNull(nameof(value))]
	string? Redact(string? value, RedactorOptions? options = null);
}

public class RedactorOptions
{
	public string? Key { get; set; }                              // where the value was found - a config key, a header name; a hint, not a decision
	public RedactionMode Mode { get; set; } = RedactionMode.Erase; // Erase: nothing survives. Mask: up to 3 characters per end survive
	public string MaskToken { get; set; } = "●●●●●●●●";           // fixed width on purpose - never leaks the length
}
```

The key-shaped call `redactor.Redact(value, "Some:Key")` is an extension method and keeps working.

### Null and empty

| Mode | `Redact(null)` | `Redact("")` |
| --- | --- | --- |
| `Erase` (default) | mask token | mask token |
| `Mask` | `null` | `""` |

Erase never says whether there was anything, so a log line cannot be read to mean "this one is not configured". Mask already reveals something about every secret it touches, so revealing that there was none is consistent with it.

### Mask never applies to a composite span

`Mask` reveals both ends of whatever it is given. That is safe on a value that is entirely a secret - a token, a GUID, a password on its own - and unsafe on anything that merely *contains* one: `user:password` masked would reveal the password's last characters. Every rule in this repo declares whether its captured span is isolated, and a composite span erases in every mode. Do not lose this when adding a rule.

## Which redactor

**`CredentialRedactor`** (in `XKit.Redactor`) is cheap: three compiled regexes for the shapes a credential is usually found in - `scheme://user:password@host`, `password=` / `pwd=` pairs, and credential-named keys in JSON. Host, database and everything else stay readable. When no rule fires and `Key` reads as a credential name, the whole value is hidden. This is the one to put inside a log sink.

**`EntropyRedactor`** (in `XKit.Redactor.Implementation`) finds secrets of unknown shape. Each alphanumeric token is split on case transitions, the pieces in the dictionary are dropped, dictionary words that ran together in one case are stripped, and the Shannon entropy of what is left decides. GUIDs and URI passwords are always hidden; hex-looking tokens are measured as bytes; a mostly-hidden base64 value collapses to one token. A credential-shaped `Key` lowers the threshold from 3.3 to 2.9 rather than deciding outright. Build the `WordDictionary` once and share it:

```csharp
services.AddSingleton(new WordDictionary(["poloniex", "apextroid"])); // product names would otherwise read as random
services.AddSingleton<IRedactor, EntropyRedactor>();
```

## String helpers

```csharp
apiKey.Mask();                          // "abc●●●●●●●●xyz" - the whole value must be the secret
message.Redact(connectionString);       // erases a known secret, and its password on its own, wherever the text quotes them
```

`Mask()` reveals nothing at 8 characters or fewer, one per end at 9-10, two at 11-12, three from 13 up. Null and empty come back unchanged, the way `RedactionMode.Mask` treats them.

## Not covered

`Microsoft.Extensions.Http`'s `RedactLoggedHeaders` runs inside the HttpClient logging handler, upstream of anything an `IRedactor` can see. Configure it with the header names that carry credentials; an empty list logs them all.

## Building

```bash
dotnet test XKit.Redactor.slnx
```

Publishing is manual: bump `<Version>` in both csproj files, then

```bash
dotnet pack XKit.Redactor.slnx -c Release
```

and `dotnet nuget push` the two `.nupkg` files.
