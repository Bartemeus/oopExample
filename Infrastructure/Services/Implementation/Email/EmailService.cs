using System;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using Smartcontract.App.Infrastructure.Services.Abstraction;
using Smartcontract.App.Infrastructure.Services.Implementation.Email.Config;

namespace Smartcontract.App.Infrastructure.Services.Implementation.Email {
	public class EmailService : IEmailService {
		private readonly EmailSenderConfig _emailSenderConfig;
		public const string EmailEventId = "EMAIL";

		public EmailService(EmailSenderConfig emailSenderConfig) {
			_emailSenderConfig = emailSenderConfig;
		}
		public void SendEmail(string email, string subject, string message) {
			try {
				using (var mailMessage = new MailMessage {
					Subject = subject,
					IsBodyHtml = true,
					BodyEncoding = Encoding.UTF8,
					HeadersEncoding = Encoding.UTF8,
					SubjectEncoding = Encoding.UTF8,
				}) {
					mailMessage.Body = message;
					mailMessage.To.Add(new MailAddress(email));
					SendEmail(mailMessage);
				}
			}
			catch (Exception ex) {
				Serilog.Log.Error(ex, "{EventId}", EmailEventId);
			}
		}

		private void SendEmail(MailMessage mailMessage) {
			// Не верь решарперу. Поле From может быть null
			if (mailMessage.From == null) {
				mailMessage.From = new MailAddress(_emailSenderConfig.FromEmail);
			}
			mailMessage.IsBodyHtml = true;
			mailMessage.BodyEncoding = mailMessage.HeadersEncoding = mailMessage.SubjectEncoding = Encoding.UTF8;
			var uid = Guid.NewGuid();
			try {
				using (var smtp = new SmtpClient {
					Host = _emailSenderConfig.SmtpAddress,
					Port = _emailSenderConfig.SmtpPort,
					EnableSsl = _emailSenderConfig.SmtpUseSsl,
					UseDefaultCredentials = false,
					Credentials = new NetworkCredential(_emailSenderConfig.SmtpAccount, _emailSenderConfig.SmtpPassword),
					DeliveryMethod = SmtpDeliveryMethod.Network,
					Timeout = 10000,
				}) {
					smtp.SendCompleted += (sender, args) => {
						if (args.Error != null) {
							Serilog.Log.Error(args.Error, "{EventId} {Uid}", EmailEventId, uid);
						}
					};
					Serilog.Log.Information(
						"{EventId} {Uid} - from: {FromEmail} to: {@Adresses} subject: {Subject} content: {Content}",
						EmailEventId, uid, mailMessage.From.Address,
						mailMessage.To.Select(x => x.Address).ToArray(),
						mailMessage.Subject,
						mailMessage.Body);
					smtp.Send(mailMessage);
				}
			}
			catch (System.Net.Mail.SmtpFailedRecipientsException failedRecipientsException) {
				Serilog.Log.Error(failedRecipientsException, "SmtpFailedRecipientsException {EventId} {Uid} - {@Adresses}", EmailEventId, uid, mailMessage.To.Select(x => x.Address).ToArray());
			}
			catch (System.Net.Mail.SmtpFailedRecipientException failedRecipientException) {
				Serilog.Log.Error(failedRecipientException, "SmtpFailedRecipientException {EventId} {Uid} - {@Adresses}", EmailEventId, uid, mailMessage.To.Select(x => x.Address).ToArray());
			}
			catch (System.Net.Mail.SmtpException smtpException) {
				Serilog.Log.Error(smtpException, "SmtpException {EventId} {Uid} - {@Adresses}", EmailEventId, uid, mailMessage.To.Select(x => x.Address).ToArray());
			}
			catch (Exception exception) {
				Serilog.Log.Error(exception, "Exception {EventId} {Uid} - {@Adresses}", EmailEventId, uid, mailMessage.To.Select(x => x.Address).ToArray());
			}
		}

	}
}
