using System;

namespace Smartcontract.App.Infrastructure.Configuration {
	public class KalkanServiceConfig {
		public string Url { get; set; }
		public TimeSpan? Timeout { get; set; }
	}
}