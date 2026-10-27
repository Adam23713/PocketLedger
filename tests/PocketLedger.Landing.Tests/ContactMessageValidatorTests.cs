using System.Net;
using MimeKit;
using PocketLedger.Landing.Contact;

namespace PocketLedger.Landing.Tests;

public sealed class ContactMessageValidatorTests
{
    private const string ValidMessage = "Thanks for building PocketLedger!";

    [Theory]
    [InlineData("visitor@example.com")]
    [InlineData("first.last+tag@sub.example.co.uk")]
    public void Validate_AcceptsPlainAddresses(string email)
    {
        var result = ContactMessageValidator.Validate($"  {email} ", ValidMessage);

        Assert.True(result.IsValid);
        Assert.Equal(email, result.Submission!.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("Visitor <visitor@example.com>")]
    [InlineData("visitor@example.com\r\nBcc: victim@example.com")]
    [InlineData("visitor@example.com,other@example.com")]
    [InlineData("\"quoted\"@example.com")]
    [InlineData("visitor@localhost")]
    public void Validate_RejectsAddressesThatAreNotASingleBareMailbox(string email)
    {
        var result = ContactMessageValidator.Validate(email, ValidMessage);

        Assert.False(result.IsValid);
        Assert.NotNull(result.EmailError);
    }

    [Theory]
    [InlineData("Hello <script>alert(1)</script> there")]
    [InlineData("Look at <img src=x onerror=alert(1)> this")]
    [InlineData("Click <a href=\"https://example.com\">here</a> please")]
    [InlineData("Try javascript:alert(document.cookie) now")]
    [InlineData("Open data:text/html;base64,PHNjcmlwdD4= now")]
    [InlineData("Encoded &lt;script&gt; payload here")]
    [InlineData("<!-- hidden comment --> visible text")]
    public void Validate_RejectsMarkupAndScriptPayloads(string message)
    {
        var result = ContactMessageValidator.Validate("visitor@example.com", message);

        Assert.False(result.IsValid);
        Assert.Contains("plain text", result.MessageError);
    }

    [Theory]
    [InlineData("I <3 this app, 2 < 3 and my data: all good")]
    [InlineData("Ár: 1 000 Ft & más — köszönöm a fejlesztést! 🎉")]
    public void Validate_AllowsOrdinaryTextThatMentionsSymbols(string message)
    {
        var result = ContactMessageValidator.Validate("visitor@example.com", message);

        Assert.True(result.IsValid, result.MessageError);
    }

    [Fact]
    public void Validate_RejectsTooManyLinks()
    {
        var result = ContactMessageValidator.Validate("visitor@example.com", "https://a.example https://b.example https://c.example www.d.example");

        Assert.False(result.IsValid);
        Assert.Contains("links", result.MessageError);
    }

    [Fact]
    public void Validate_EnforcesLengthLimits()
    {
        Assert.False(ContactMessageValidator.Validate("visitor@example.com", "Too short").IsValid);
        Assert.False(ContactMessageValidator.Validate("visitor@example.com", new string('a', ContactMessageValidator.MaximumMessageLength + 1)).IsValid);
        Assert.False(ContactMessageValidator.Validate(new string('a', 250) + "@example.com", ValidMessage).IsValid);
    }

    [Fact]
    public void Validate_StripsInvisibleCharactersAndNormalizesLineBreaks()
    {
        var result = ContactMessageValidator.Validate("visitor@example.com", "  Hello\u200B\u202Eworld\u0000\r\n\r\n\r\n\r\nSecond\u2028line  ");

        Assert.True(result.IsValid);
        Assert.Equal("Helloworld\n\nSecondline", result.Submission!.Message);
    }

    [Fact]
    public void ContactMail_UsesFixedSenderAndPlainTextBody()
    {
        var settings = new ContactOptions { Smtp = new ContactSmtpOptions { FromAddress = "noreply@pocketledger.dev" } };
        var submission = new ContactSubmission("visitor@example.com", "Line one\nLine two");

        var mail = ContactMail.Create(settings, submission, new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal("noreply@pocketledger.dev", Assert.Single(mail.From.Mailboxes).Address);
        Assert.Equal("info@pocketledger.dev", Assert.Single(mail.To.Mailboxes).Address);
        Assert.Equal("visitor@example.com", Assert.Single(mail.ReplyTo.Mailboxes).Address);
        var body = Assert.IsType<TextPart>(mail.Body);
        Assert.True(body.IsPlain);
        Assert.Contains("Line one\nLine two", body.Text.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("visitor@example.com", mail.Subject);
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    public void ClientKey_GroupsIpv6ClientsByPrefix(string address, string expected)
    {
        Assert.Equal(expected, ContactRateLimiter.GetClientKey(IPAddress.Parse(address)));
    }
}
