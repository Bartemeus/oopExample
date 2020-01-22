namespace Smartcontract.App.Infrastructure.Services.Implementation.Email.Config {
	public class EmailSenderConfig {
		public string SmtpAddress { get; set; }
		public int SmtpPort { get; set; }
		public string SmtpAccount { get; set; }
		public string SmtpPassword { get; set; }
		public bool SmtpUseSsl { get; set; }
		public string FromEmail { get; set; }
        public string InfoEmail { get; set; }
	}
}
