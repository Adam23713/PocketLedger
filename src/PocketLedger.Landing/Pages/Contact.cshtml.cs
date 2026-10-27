using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using PocketLedger.Landing.Contact;

namespace PocketLedger.Landing.Pages;

[RequestSizeLimit(32 * 1024)]
[RequestFormLimits(ValueCountLimit = 16, ValueLengthLimit = 16 * 1024)]
public sealed class ContactModel(
    IOptions<ContactOptions> options,
    IWebHostEnvironment environment,
    IContactRateLimiter rateLimiter,
    ContactFormTimestamp formTimestamp,
    ITurnstileVerifier turnstile,
    IContactMailSender mailSender,
    ILogger<ContactModel> logger) : PageModel
{
    public const string TurnstileResponseField = "cf-turnstile-response";

    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? Message { get; set; }
    // Honeypot: hidden from people, but naive bots fill in every field they find.
    [BindProperty] public string? Website { get; set; }
    [BindProperty] public string? FormStamp { get; set; }

    public bool Sent { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? TurnstileSiteKey => options.Value.Turnstile.SiteKey;

    // Outside Development the form only accepts messages when both bot protection and mail delivery are configured.
    public bool IsAvailable => options.Value.Turnstile.IsConfigured && (options.Value.Smtp.IsConfigured || environment.IsDevelopment());

    public void OnGet(bool sent = false)
    {
        Sent = sent;
        FormStamp = formTimestamp.Issue();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable) return Fail(StatusCodes.Status503ServiceUnavailable, "The contact form is not available right now. Please try again later.");

        var clientIp = ResolveClientIp();
        if (!rateLimiter.TryAcquireAttempt(ContactRateLimiter.GetClientKey(clientIp)))
            return Fail(StatusCodes.Status429TooManyRequests, "You have sent several messages recently. Please wait an hour before trying again.");

        if (!string.IsNullOrEmpty(Website))
        {
            logger.LogInformation("A contact form submission was dropped because the honeypot field was filled in.");
            return RedirectToPage(new { sent = true });
        }

        if (!formTimestamp.IsAcceptable(FormStamp))
            return Fail(StatusCodes.Status400BadRequest, "This form has expired or was sent too quickly. Please check your message and send it again.");

        var validation = ContactMessageValidator.Validate(Email, Message);
        if (!validation.IsValid)
        {
            if (validation.EmailError is not null) ModelState.AddModelError(nameof(Email), validation.EmailError);
            if (validation.MessageError is not null) ModelState.AddModelError(nameof(Message), validation.MessageError);
            return Fail(StatusCodes.Status400BadRequest, null);
        }

        if (!await turnstile.VerifyAsync(Request.Form[TurnstileResponseField], clientIp?.ToString(), cancellationToken))
            return Fail(StatusCodes.Status400BadRequest, "The anti-spam check could not be completed. Please wait for it to finish and send the message again.");

        if (!rateLimiter.TryAcquireDelivery(validation.Submission!.Email))
            return Fail(StatusCodes.Status429TooManyRequests, "The contact form is receiving a lot of messages right now. Please try again later.");

        try
        {
            await mailSender.SendAsync(validation.Submission, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "A contact form message could not be delivered.");
            return Fail(StatusCodes.Status503ServiceUnavailable, "Your message could not be sent right now. Please try again a little later.");
        }

        return RedirectToPage(new { sent = true });
    }

    private PageResult Fail(int statusCode, string? message)
    {
        ErrorMessage = message;
        FormStamp = formTimestamp.Issue();
        Response.StatusCode = statusCode;
        return Page();
    }

    private IPAddress? ResolveClientIp()
    {
        var header = options.Value.ClientIpHeader;
        if (!string.IsNullOrWhiteSpace(header) && IPAddress.TryParse(Request.Headers[header].ToString().Trim(), out var forwarded)) return forwarded;
        return HttpContext.Connection.RemoteIpAddress;
    }
}
