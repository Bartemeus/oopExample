using Smartcontract.DataContracts.Registration;

namespace Smartcontract.App.Infrastructure.Services.Abstraction {
	public interface IOrgnaizationsInformationService {
		ClientInformation GetClientInformation(string xin);
	}
}
