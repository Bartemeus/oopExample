using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Smartcontract.App.Infrastructure.Services.Abstraction {
	public interface IPhoneConfirmationService {
		Task<bool> SendConfirmationCodeAsync(string phoneNumber);
		Task<bool> ValidateConfirmationCodeAsync(string phoneNumber, string smsCode);
	}
}
