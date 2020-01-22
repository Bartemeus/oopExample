using System.Threading.Tasks;

namespace Smartcontract.App.Infrastructure.Services.Abstraction {
	public interface ICryptoProviderService {
		Task<string> SignXml(string xml);
		Task<bool> VerifyXml(string xml);
        /// <summary>
        /// Валидация подписи CMS
        /// </summary>
        /// <param name="data">Исходные данные без подписи в формате xml</param>
        /// <param name="cms">Подпись cms для валидации</param>
        /// <returns>Является ли подпись валидной</returns>
		Task<bool> VerifyCMSAsync(string data, string cms);

        Task<bool> VerifyExpiredCert(string cms);
    }
}