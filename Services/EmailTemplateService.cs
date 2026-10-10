using System.Net;
using Microsoft.Extensions.Options;

namespace HikingStore.Services
{
    public interface IEmailTemplateService
    {
        /// <param name="callbackUrl">Already HTML-encoded confirmation URL.</param>
        /// <param name="userName">Already HTML-encoded greeting name.</param>
        string GenerateEmailConfirmationTemplate(string callbackUrl, string userName);

        /// <summary>A short branded email with one call-to-action button. All arguments are plain text and are encoded here.</summary>
        string GenerateActionEmail(string heading, string message, string buttonText, string url, string? footnote = null);
    }

    public class EmailTemplateService : IEmailTemplateService
    {
        private readonly StoreSettings _store;

        public EmailTemplateService(IOptions<StoreSettings> store)
        {
            _store = store.Value;
        }

        public string GenerateEmailConfirmationTemplate(string callbackUrl, string userName) =>
            Layout(
                "Confirm your email address",
                $@"<p style=""margin:0 0 16px;line-height:1.5"">Hi {userName},</p>
<p style=""margin:0 0 16px;line-height:1.5"">Thanks for creating a {E(_store.Name)} account. Please confirm your email address to activate it.</p>
{Button("Confirm email address", callbackUrl)}
<p style=""margin:0 0 8px;color:#73786F;font-size:13px;line-height:1.5"">If the button doesn't work, copy this link into your browser:<br><a href=""{callbackUrl}"" style=""color:#234B35;word-break:break-all"">{callbackUrl}</a></p>
<p style=""margin:16px 0 0;color:#73786F;font-size:13px;line-height:1.5"">If you didn't create an account, you can ignore this email.</p>");

        public string GenerateActionEmail(string heading, string message, string buttonText, string url, string? footnote = null)
        {
            var encodedUrl = E(url);
            var note = string.IsNullOrWhiteSpace(footnote)
                ? string.Empty
                : $@"<p style=""margin:16px 0 0;color:#73786F;font-size:13px;line-height:1.5"">{E(footnote)}</p>";
            return Layout(heading,
                $@"<p style=""margin:0 0 16px;line-height:1.5"">{E(message)}</p>
{Button(buttonText, encodedUrl)}
<p style=""margin:0;color:#73786F;font-size:13px;line-height:1.5"">If the button doesn't work, copy this link into your browser:<br><a href=""{encodedUrl}"" style=""color:#234B35;word-break:break-all"">{encodedUrl}</a></p>
{note}");
        }

        private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        private static string Button(string text, string encodedUrl) =>
            $@"<p style=""margin:24px 0""><a href=""{encodedUrl}"" style=""background:#234B35;color:#fff;padding:12px 20px;border-radius:6px;text-decoration:none;font-weight:600;display:inline-block"">{E(text)}</a></p>";

        private string Layout(string heading, string innerHtml)
        {
            var contact = new List<string>();
            if (!string.IsNullOrWhiteSpace(_store.SupportEmail)) contact.Add(E(_store.SupportEmail));
            if (!string.IsNullOrWhiteSpace(_store.SupportPhone)) contact.Add(E(_store.SupportPhone));
            if (!string.IsNullOrWhiteSpace(_store.Address)) contact.Add(E(_store.Address));

            return $@"<!DOCTYPE html><html><body style=""margin:0;background:#F3EFE5;font-family:Segoe UI,Arial,sans-serif;color:#252A25"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0""><tr><td align=""center"" style=""padding:24px"">
<table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;background:#fff;border-radius:8px;overflow:hidden"">
<tr><td style=""background:#163526;color:#fff;padding:24px;font-size:22px;font-weight:700"">{E(_store.Name)}</td></tr>
<tr><td style=""padding:24px"">
<h1 style=""font-size:20px;margin:0 0 16px"">{E(heading)}</h1>
{innerHtml}
</td></tr>
<tr><td style=""padding:16px 24px;background:#F3EFE5;color:#73786F;font-size:12px;line-height:1.5"">{string.Join(" · ", contact)}</td></tr>
</table></td></tr></table></body></html>";
        }
    }
}
