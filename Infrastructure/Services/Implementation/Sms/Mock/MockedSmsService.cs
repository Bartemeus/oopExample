using System.Threading.Tasks;
using Smartcontract.App.Infrastructure.Services.Abstraction;

namespace Smartcontract.App.Infrastructure.Services.Implementation.Sms.Mock {
    public class MockedSmsService : ISmsService {
        public Task<bool> SendAsync(string phone, string text) {
            return Task.FromResult(true);
        }
    }
}