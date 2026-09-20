# XKit.Redactor

One contract for taking secrets out of text before it goes somewhere it cannot be recalled from - a log file, a notification, an admin page - and two ways of finding them.

| Package | Targets | What is in it |
| --- | --- | --- |
| `XKit.Redactor` | netstandard2.0, netstandard2.1, net8.0, net9.0, net10.0 | `IRedactor`, `RedactorOptions`, `RedactionMode`, the rule-based `CredentialRedactor`, `RedactorException`, `Mask()` and `Redact(secret)` string helpers |
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
	public RedactionMode Mode { get; set; } = RedactionMode.Erase; // Erase / Mask / Label - see below
	public string MaskToken { get; set; } = "●●●●●●●●";           // fixed width on purpose - never leaks the length
	public string? Label { get; set; }                            // what Label mode says instead of the value
}
```

The key-shaped call `redactor.Redact(value, "Some:Key")` is an extension method and keeps working.

### The three modes

`Erase` and `Mask` trade off how much of the *value* survives. `Label` is a different axis: nothing of the value survives, and something about its *context* is said instead.

| Mode | Reveals about the value | Reveals about the context |
| --- | --- | --- |
| `Erase` (default) | nothing | nothing |
| `Mask` | up to 3 characters per end | nothing |
| `Label` | nothing | what it was, or why it is hidden |

### Null and empty

| Mode | `Redact(null)` | `Redact("")` |
| --- | --- | --- |
| `Erase` (default) | mask token | mask token |
| `Mask` | `null` | `""` |
| `Label` | the labelled token, exactly as for a real value | same |

**`Mask` is the only mode that passes anything through**, because it is the only mode that reveals anything. Erase never says whether there was anything, so a log line cannot be read to mean "this one is not configured".

Label keeps that same property, which is why it does *not* simply return the plain token: under `Key = "Poloniex:ApiKey"`, a null, an empty string and a real key all render `●●●Poloniex:ApiKey●●●`. If null fell back to the bare token you could tell "unset" from "set" by looking, which is the leak the contract exists to prevent. Only when no label resolves at all does it fall back to the plain token — and then there is nothing to tell apart.

### Mask never applies to a composite span

`Mask` reveals both ends of whatever it is given. That is safe on a value that is entirely a secret - a token, a GUID, a password on its own - and unsafe on anything that merely *contains* one: `user:password` masked would reveal the password's last characters. Every rule in this repo declares whether its captured span is isolated, and a composite span erases in every mode. Do not lose this when adding a rule.

### `Label`: saying why, without saying what

"Hidden because secret" and "hidden because broken" otherwise look identical to whoever reads the log — different faults needing different fixes, reported the same way. `Label` wraps a label in the mask token's ends instead of the value:

```
●●●●●●●●                    →  ●●●MongoConfigurationException●●●
mongodb://●●●●●●●●@host/db  →  mongodb://●●●userinfo●●●@host/db
{ "ApiKey": "●●●●●●●●" }    →  { "ApiKey": "●●●apikey●●●" }
```

The label resolves as **`options.Label` → `options.Key` → the name of the rule that matched**, so the mode is useful with nobody passing anything and exact where somebody does. With none of the three resolvable it falls back to the plain token, i.e. to `Erase`.

> **The label must be something the code chose — a key name, a rule name, a type name. Never anything read out of the value.**

`ex.GetType().Name` is safe. `ex.Message` is not, and neither is the value's length, its first characters, or whether it was null. Every rule in this repo labels itself with a constant for exactly that reason. A label *does* disclose the kind of secret — `●●●apikey●●●` says an API key was there — which is the point of the mode rather than a side effect, but it is a decision on the record. `Label` and `Mask` are mutually exclusive: a label and a partial reveal in one output is nothing anybody wants.

Where the caller has already isolated the value and no rule could match it — a connection string too malformed to parse — hide it whole:

```csharp
catch (MongoConfigurationException ex)
{
	// nothing safe to say about the string itself, so say why instead
	return new RedactorOptions { Mode = RedactionMode.Label, Label = ex.GetType().Name }
		.Hide(connectionString, isolated: false);
}
```

## Which redactor

**`CredentialRedactor`** (in `XKit.Redactor`) is cheap: three compiled regexes for the shapes a credential is usually found in - `scheme://user:password@host`, `password=` / `pwd=` pairs, and credential-named keys in JSON. Host, database and everything else stay readable. When no rule fires and `Key` reads as a credential name, the whole value is hidden. This is the one to put inside a log sink.

**`EntropyRedactor`** (in `XKit.Redactor.Implementation`) finds secrets of unknown shape. Each alphanumeric token is split on case transitions, the pieces in the dictionary are dropped, dictionary words that ran together in one case are stripped, and the Shannon entropy of what is left decides. GUIDs and URI passwords are always hidden; hex-looking tokens are measured as bytes; a mostly-hidden base64 value collapses to one token. A credential-shaped `Key` lowers the threshold from 3.3 to 2.9 rather than deciding outright. Build the `WordDictionary` once and share it:

```csharp
services.AddSingleton(new WordDictionary(["poloniex", "apextroid"])); // product names would otherwise read as random
services.AddSingleton<IRedactor, EntropyRedactor>();
```

## Wrapping a failure: `RedactorException`

Hiding a secret is a legitimate reason to wrap an exception. It is never a reason to drop one — so this type has **no constructor that omits the inner exception**, and the message is redacted on construction so a call site cannot hand it raw text:

```csharp
catch (MongoConfigurationException ex)
{
	throw new RedactorException($"ConnectionStrings:Default is not valid ({connectionString})", ex);
}
```

The redactor is the third parameter and optional — `CredentialRedactor.Default` when omitted, which is the right answer where there is nothing to inject. `ToString()` runs the redactor over the **whole formatted chain**, so a secret quoted by the *inner* exception's message never reaches a log sink either. It is a sealed override and there is no switch to disable it: the cost is one redactor pass over text that already cost a stack-trace materialisation, and a switch is what someone flips while chasing a number in a log loop.

**Throw it from the validation site, not from a `ToString()`.** A describer — a `ToString()` override, a "connection description" property — is usually formatted *by* the log line that is trying to report the problem, so throwing from one breaks the reporting instead of improving it, and can recurse. Validate where the value enters and throw there; let the describer fall back to the mask token, because by then the validation site has already failed loudly. (Found by ApexTroid adopting this: the obvious-looking place for the Mongo example was `MongoConnectionDescription.ToString()`, and the right place was `RequireUrl`.)

**Check your library first.** A well-behaved one already redacts its own messages. Verified for MongoDB.Driver 3.11.2: every malformed connection string throws `MongoConfigurationException` with the password absent from both `Message` and `ToString()` — `mongodb://<hidden>@host/db`. Note that `MongoUrl.ToString()` on a *valid* url does round-trip the password in full, so the redaction is in the error path only. Write a test that fails if any of that changes.

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

Publishing is automatic: bump `<Version>` in both csproj files and push to `master`. The `XKit.Redactor` pipeline in the `GitHubNugets` Azure DevOps project builds, tests, packs and pushes both packages to nuget.org.
