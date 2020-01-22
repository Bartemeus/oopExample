using System.Threading.Tasks;
using Smartcontract.App.Infrastructure.Services.Implementation;
using Smartcontract.DAL.Entities;
using Smartcontract.DataContracts.Jwt;
using Smartcontract.DataContracts.Registration;

namespace Smartcontract.App.Infrastructure.Services.Abstraction {
	public interface IAuthenticationManager {
		Task<JwtExtendedResponse> AuthenticateWithRefreshAsync(string login, string password);
		Task<JwtExtendedResponse> RefreshAccessToken(string refreshToken);
		Task RevokeRefreshToken(string refreshToken);
		Task RegisterAsync(RegistrationRequest request, RemoteBillingService billingService);
		Task EditPasswordAsync(UserAccountRequest request);
        Task<bool> PasswordsMatched(User user, string newPassword);
        Task EditPhoneAsync(UserAccountRequest request);
    }
}
