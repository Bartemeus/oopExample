using System.Threading.Tasks;

namespace Smartcontract.App.Infrastructure.Services.Abstraction {
	public interface IEmailService {
		void SendEmail(string email, string subject, string message);
	}
}