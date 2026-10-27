using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace PocketLedger.Landing.Contact;

public sealed record ContactSubmission(string Email, string Message);

public sealed record ContactValidationResult(ContactSubmission? Submission, string? EmailError, string? MessageError)
{
    public bool IsValid => Submission is not null;
}

// The message only ever leaves this service as a text/plain mail body, but it is still normalized and screened so that nothing
// resembling markup, script or invisible control characters reaches the inbox in the first place.
public static partial class ContactMessageValidator
{
    public const int MaximumEmailLength = 254;
    public const int MinimumMessageLength = 10;
    public const int MaximumMessageLength = 4000;
    public const int MaximumLinkCount = 3;

    public static ContactValidationResult Validate(string? email, string? message)
    {
        var normalizedEmail = NormalizeEmail(email, out var emailError);
        var normalizedMessage = NormalizeMessage(message, out var messageError);
        return emailError is null && messageError is null
            ? new ContactValidationResult(new ContactSubmission(normalizedEmail!, normalizedMessage!), null, null)
            : new ContactValidationResult(null, emailError, messageError);
    }

    private static string? NormalizeEmail(string? value, out string? error)
    {
        error = null;
        var email = value?.Trim() ?? string.Empty;
        if (email.Length == 0) error = "Enter the email address I should reply to.";
        else if (email.Length > MaximumEmailLength) error = "That email address is too long.";
        // A bare address only: display names, quoting, comments and line breaks are what header injection is built from.
        else if (!SimpleAddressPattern().IsMatch(email) || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || !string.IsNullOrEmpty(parsed.DisplayName))
            error = "Enter a valid email address, like name@example.com.";
        return error is null ? email : null;
    }

    private static string? NormalizeMessage(string? value, out string? error)
    {
        error = null;
        var message = Clean(value ?? string.Empty);
        if (message.Length < MinimumMessageLength) error = $"Write a message of at least {MinimumMessageLength} characters.";
        else if (message.Length > MaximumMessageLength) error = $"Keep the message under {MaximumMessageLength} characters.";
        else if (MarkupPattern().IsMatch(message) || ScriptSchemePattern().IsMatch(message))
            error = "Please send plain text only — HTML, scripts and code snippets are not accepted.";
        else if (LinkPattern().Count(message) > MaximumLinkCount) error = $"Please include no more than {MaximumLinkCount} links.";
        return error is null ? message : null;
    }

    private static string Clean(string value)
    {
        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n').Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (character is '\n' or '\t' || !IsHidden(character)) builder.Append(character);
        }
        return ExcessBlankLinesPattern().Replace(builder.ToString(), "\n\n").Trim();
    }

    // Control, format (zero-width and bidirectional overrides) and unassigned characters can disguise content, so they are dropped.
    private static bool IsHidden(char character) => CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.Control
        or UnicodeCategory.Format or UnicodeCategory.OtherNotAssigned or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    [GeneratedRegex(@"^[^\s@<>()\[\]\\,;:""]{1,64}@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$")]
    private static partial Regex SimpleAddressPattern();

    [GeneratedRegex(@"<\s*/?\s*[a-z!?][^>]*>|&(?:#[0-9]+|#x[0-9a-f]+|[a-z]{2,8});", RegexOptions.IgnoreCase)]
    private static partial Regex MarkupPattern();

    [GeneratedRegex(@"\b(?:java|vb)script\s*:|\bdata\s*:\s*[a-z]+/[a-z0-9.+-]+", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptSchemePattern();

    [GeneratedRegex(@"https?://|www\.", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessBlankLinesPattern();
}
