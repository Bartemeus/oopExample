using System.Threading.Tasks;

namespace Smartcontract.App.Infrastructure.Services.Abstraction {
    public interface ISmsService {
        Task<bool> SendAsync(string phone, string text);
    }
}