using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using Smartcontract.App.Infrastructure.Services.Abstraction;
using Smartcontract.DAL.Entities;

namespace Smartcontract.App.Infrastructure.Services.Implementation {
	public class EmailConfirmationService : IEmailConfirmationService {
		private readonly IEmailService _emailService;
		private readonly TimeSpan _codeValidPeriod = TimeSpan.FromHours(24);
		private static string _key = "smartcontract.kz";
		public AesCryptoServiceProvider Protector { get; private set; }

		public EmailConfirmationService(IEmailService emailService) {
			_emailService = emailService;
			Protector = new AesCryptoServiceProvider() { };
		}
		public bool SendConfirmationUrl(string email, string callbackUrl) {
			_emailService.SendEmail(email, "Подтверждение аккаунта smartcontract.kz", $"Подтвердите регистрацию, перейдя по ссылке: <a href='{callbackUrl}'>smartcontract.kz</a>");
			return true;
		}

		public bool ValidateConfirmationCode(User user, string hashCode) {
			try {
				var data = Convert.FromBase64String(hashCode);
				using (var aesAlg = Aes.Create()) {
					aesAlg.Key = Encoding.UTF8.GetBytes(_key);
					aesAlg.IV = new byte[16];
					var decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
					using (var msDecrypt = new MemoryStream(data)) {
						using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read)) {
							using (var srDecrypt = new StreamReader(csDecrypt)) {

								var decodedData = srDecrypt.ReadToEnd();
								var decodedDataSplit = decodedData.Split(new[] { '|' });
								var creationTime = new DateTimeOffset(Convert.ToInt64(decodedDataSplit[0]), TimeSpan.Zero);
								var expirationTime = creationTime + this._codeValidPeriod;
								if (expirationTime < DateTimeOffset.UtcNow) {
									return false;
								}
								var userId = decodedDataSplit[1];
								var password = decodedDataSplit[2];
								if (!string.Equals(userId, user.Id.ToString())) {
									return false;
								}
								if (password != user.Password) {
									return false;
								}
								return true;
							}
						}
					}
				}
			}
			catch (Exception ex) {
				Log.Error(ex, "Confirmation code validation error: {user}", user.UserName);
			}
			return false;
		}

		public string GenerateEmailConfirmationToken(User user) {
			if (user == null) {
				throw new ArgumentNullException(nameof(user));
			}

			var encrypted = new byte[0];
			using (var aesAlg = Aes.Create()) {
				aesAlg.Key = Encoding.UTF8.GetBytes(_key);
				aesAlg.IV = new byte[16];
				var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);
				using (var msEncrypt = new MemoryStream()) {
					using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write)) {
						using (var swEncrypt = new StreamWriter(csEncrypt)) {
							swEncrypt.Write($"{DateTimeOffset.UtcNow.UtcTicks}|{user.Id}|{user.Password}");
						}
						encrypted = msEncrypt.ToArray();
					}
				}
			}

			return Convert.ToBase64String(encrypted);
		}

		public bool SendForgotPasswordUrl(string email, string callbackUrl) {
			_emailService.SendEmail(email, "Запрос на изменение пароля smartcontract.kz", $"Для изменения пароля, перейдя по ссылке: <a href='{callbackUrl}'>smartcontract.kz</a>");
			return true;
		}
	}
}