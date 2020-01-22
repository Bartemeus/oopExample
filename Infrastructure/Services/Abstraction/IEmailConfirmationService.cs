using System.Threading.Tasks;
using Smartcontract.DAL.Entities;

namespace Smartcontract.App.Infrastructure.Services.Abstraction {
	public interface IEmailConfirmationService {
		bool SendConfirmationUrl(string email, string callbackUrl);
		bool ValidateConfirmationCode(User user, string hashCode);
		string GenerateEmailConfirmationToken(User user);
		bool SendForgotPasswordUrl(string email, string callbackUrl);
	}
}