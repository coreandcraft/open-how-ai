using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Encodings.Web;
using Journey.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Journey.Server.Features.Accounts;

public class AccountEmail(IConfiguration config, IWebHostEnvironment environment)
{
    public string Origin => (config["Site:PublicUrl"] ?? "http://127.0.0.1:5080").TrimEnd('/');

    public async Task Confirmation(User user, UserManager<User> users)
    {
        var code = Encode(await users.GenerateEmailConfirmationTokenAsync(user));
        await Send(user.Email!, "Confirm your email", "One small step before your next big idea.",
            "Confirm your email address to start collecting conversations and sharing the story behind your work.",
            $"{Origin}/#confirm-email?userId={Uri.EscapeDataString(user.Id)}&code={code}", "Confirm email");
    }

    public async Task Reset(User user, UserManager<User> users)
    {
        var code = Encode(await users.GeneratePasswordResetTokenAsync(user));
        await Send(user.Email!, "Reset your password", "Let's get you back to your work.",
            "We received a request to reset your openhow password. Choose a new password using the button below.",
            $"{Origin}/#reset-password?email={Uri.EscapeDataString(user.Email!)}&code={code}", "Reset password");
    }

    public static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    public static string Decode(string token) => Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));

    public virtual async Task Send(string to, string subject, string heading, string message, string url, string action)
    {
        static string H(string text) => HtmlEncoder.Default.Encode(text);
        var plain = $"openhow\n\n{heading}\n\n{message}\n\n{action}: {url}\n\nThis link expires in two hours. If you didn't request this, you can ignore this email.\n\nThe story behind the work.";
        var html = $"""
            <!doctype html><html lang="en"><head><meta name="viewport" content="width=device-width, initial-scale=1"></head>
            <body style="margin:0;background:#f3f5fa;color:#18243b;font-family:Arial,sans-serif">
            <div style="display:none;max-height:0;overflow:hidden">{H(subject)} to continue your journey with openhow.</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:40px 16px">
            <table role="presentation" width="560" cellpadding="0" cellspacing="0" style="width:100%;max-width:560px;background:#ffffff;border:1px solid #dde3ef;border-radius:16px">
            <tr><td style="padding:32px"><div style="font-size:25px;font-weight:bold;color:#325ae8">openhow</div>
            <p style="font-size:11px;letter-spacing:2px;color:#697791">THE STORY BEHIND THE WORK</p>
            <h1 style="font-size:27px;line-height:1.25;margin-top:32px">{H(heading)}</h1>
            <p style="font-size:16px;line-height:1.7;color:#52617a">{H(message)}</p>
            <table role="presentation" cellpadding="0" cellspacing="0"><tr><td bgcolor="#325ae8" style="border-radius:8px">
            <a href="{H(url)}" style="display:inline-block;padding:15px 24px;color:#ffffff;font-size:16px;text-decoration:none;font-weight:bold">{H(action)}</a>
            </td></tr></table>
            <p style="font-size:13px;line-height:1.7;color:#697791;margin-top:28px">This link expires in two hours. If you didn't request this, you can safely ignore this email.</p>
            <p style="font-size:12px;line-height:1.7;color:#697791">Button not working? Copy this link into your browser:<br><a href="{H(url)}" style="color:#325ae8;word-break:break-all">{H(url)}</a></p>
            </td></tr></table><p style="font-size:12px;color:#697791">openhow · Preserve the conversations. Connect the decisions.</p>
            </td></tr></table></body></html>
            """;
        using var mail = new MailMessage { From = new MailAddress(config["Email:From"] ?? "hello@openhow.local", "openhow"), Subject = subject + " · openhow", Body = plain };
        mail.To.Add(to);
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, "text/html"));
        using var smtp = new SmtpClient();
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            if (string.IsNullOrWhiteSpace(config["Email:Host"]))
            {
                var path = Path.GetFullPath(config["Email:PickupDirectory"] ?? Path.Combine(environment.ContentRootPath, "data", "mail"));
                Directory.CreateDirectory(path);
                smtp.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
                smtp.PickupDirectoryLocation = path;
                await smtp.SendMailAsync(mail);
                return;
            }
        }
        smtp.Host = config["Email:Host"]!;
        smtp.Port = config.GetValue("Email:Port", 587);
        smtp.EnableSsl = true;
        if (!string.IsNullOrEmpty(config["Email:Username"]))
            smtp.Credentials = new NetworkCredential(config["Email:Username"], config["Email:Password"]);
        await smtp.SendMailAsync(mail);
    }
}
