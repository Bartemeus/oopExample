using System.Threading.Tasks;
using Fdo.Web.Infrastructure.Services.Sms.SmsCenterKz.Config;
using Smartcontract.App.Infrastructure.Services.Abstraction;

namespace Smartcontract.App.Infrastructure.Services.Implementation.Sms.SmsCenterKz {
    public class SmsCenterService : ISmsService {
        private readonly string _login;
        private readonly string _password;

        public SmsCenterService(SmsCenterConfig config) : this(config.Login, config.Password) {

        }
        private SmsCenterService(string login, string password) {
            _login = login;
            _password = password;
        }
        public async Task<bool> SendAsync(string phone, string text) {
            var smsCenter = new SMSC(_login, _password);
            await Task.Run(() => smsCenter.send_sms(FormatPhone(phone), text));
            return true;
        }

		private string FormatPhone(string phone) {
			var prefix = "+7";
			if (phone.StartsWith(prefix)) {
				return phone;
			}
			return prefix + phone;
		}
	}
}