using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using Common.DAL.Abstraction.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Migrations;
using Smartcontract.App.Infrastructure.Services.Abstraction;
using Smartcontract.App.Managers;
using Smartcontract.Constants;
using Smartcontract.DataContracts;
using Smartcontract.DataContracts.Registration;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities;
using Serilog;

namespace Smartcontract.App.Infrastructure.Services.Implementation {
	public class InviteService : IInviteService {
		private Provider _provider;
		private readonly TimeSpan _codeValidPeriod = TimeSpan.FromHours(24);
		private IEmailService _emailService;
		private static string _key = "smartcontract.kz";
		public InviteService(Provider provider, IEmailService emailService) {
			_provider = provider;
			_emailService = emailService;
		}
		public async Task<bool> RegisterContrager(InviteRequest request, string url,string emailSender) {
			using (var repository = new Repository<User>(_provider)) {
				var profileManager = new ProfileManager(_provider);
                var notificationSettingsRep = new Repository<NotificationSettings>(repository);
				request.InviteClientRequest.ContragentType =
					request.InviteClientRequest.IsJuridic ? ContragentTypeEnum.Legal : request.InviteClientRequest.ContragentType;
				await profileManager.CreateContraget(request);
				var message =
					$"Пользователь {request.SenderFullName}, {request.SenderXin} приглашает Вас участвовать в двухстороннем подписании электронных документов в системе Smartcontract.kz. Комментарий от отправителя:{request.InviteClientRequest.Comment}. Для завершения регистрации, пожалуйста, перейдите по следующей ссылке  <a href='{url}'>smartcontract.kz</a>";
				_emailService.SendEmail(request.InviteClientRequest.Email, "Приглашение на smartcontract.kz", message);
                message =
                    $"Вы отправили приглашение пользователю {request.InviteClientRequest.FullName}, {request.InviteClientRequest.Xin} на email {request.InviteClientRequest.Email}, со следующим комментарием: «{request.InviteClientRequest.Comment}».";
                var notificationSettingsSender = notificationSettingsRep.Get(x => x.User.Email == emailSender).Single();
                if (notificationSettingsSender.InviteSend) {
                    _emailService.SendEmail(emailSender, "Вы отправили приглашение на Smartcontract.kz", message);
                }
                return true;
			}
		}


		public string GenerateCode(string xin) {
			var code = "";
			var encrypted = new byte[0];
			using (var aesAlg = Aes.Create()) {
				aesAlg.Key = Encoding.UTF8.GetBytes(_key);
				aesAlg.IV = new byte[16];
				var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);
				using (var msEncrypt = new MemoryStream()) {
					using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write)) {
						using (var swEncrypt = new StreamWriter(csEncrypt)) {
							swEncrypt.Write($"{DateTimeOffset.UtcNow.UtcTicks}|{xin}");
						}
						encrypted = msEncrypt.ToArray();
					}
				}
			}
			return Convert.ToBase64String(encrypted);
		}

		public string DecodeCode(string hashCode) {
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
								var userId = decodedDataSplit[1];

								return userId;
							}
						}
					}
				}
			} catch (Exception ex) {
                Log.Error("DecodeCode:{0}", ex.Message);
			}
			return string.Empty;
		}
	}
}
