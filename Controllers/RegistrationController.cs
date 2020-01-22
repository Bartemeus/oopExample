using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Common.DAL.Abstraction.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NHibernate.Linq;
using Smartcontract.App.Infrastructure.Services.Abstraction;
using Smartcontract.App.Infrastructure.Services.Implementation;
using Common.DataContracts.Base;
using Smartcontract.DataContracts.Registration;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities;
using Smartcontract.Common.Certificates;
using Smartcontract.Common.Encryption;
using Smartcontract.Common.Serialization;
using Smartcontract.DataContracts.Attribute;

namespace Smartcontract.App.Controllers {
	[Route("api/[controller]")]
	[Authorize]
	public class RegistrationController : Controller {
		private readonly Provider _provider;      
		public RegistrationController(Provider provider) {
			_provider = provider;         
        }

        /// <summary>
        /// Получение зашифрованного timestamp при открытии страницы регистрации
        /// </summary>
        /// <returns>Зашифрованный timestamp, обернутый в xml</returns>
        [HttpGet("[action]")]
        [AllowAnonymous]
        [ProducesResponseType(200, Type = typeof(ApiResponse<string>))]
        public IActionResult GetSignedXml()
        {
            var timestamp = DateTime.Now.Ticks;
            var encrypted = AesHelper.Encrypt(timestamp.ToString());
            var xml = XmlSerializationHelper.SerializeToXml(encrypted);
            var base64 = Convert.ToBase64String(new UTF8Encoding(false).GetBytes(xml));
            return Json(ApiResponse.Success(base64));
        }

        /// <summary>
        /// Метод, возвращающий информацию о владельце ЭЦП
        /// </summary>
        /// <param name="request">ДТОха с подписью cms</param>
        /// <returns>Объект CertificateInfo с информацией о владельце ЭЦП</returns>
        [HttpPost("[action]")]
		[AllowAnonymous]
		[ProducesResponseType(200, Type = typeof(ApiResponse<string>))]
		public async Task<IActionResult> GetSubjectData([FromBody] RegistrationCmsRequest request,
            [FromServices]ICryptoProviderService cryptoProvider) {
			var signedCms = CertificatesHelper.DecodeCmsFromString(request.Cms);
            var OSCPval = await cryptoProvider.VerifyExpiredCert(request.Cms);

            if (OSCPval)
            {
                return Json(ApiResponse.Failed(ApiErrorCode.ValidationError, "Отозванный сертификат невозможно использовать в системе"));
            }

            if (signedCms.Certificates.Count == 0) {
				return Json(ApiResponse.Failed(ApiErrorCode.ResourceNotFound, "У сертификата отсутствуют данные"));
			}
			var certificate = signedCms.Certificates[0];
			var data = CertificatesHelper.ReadX509CertificateCommonData(certificate);
			return Json(ApiResponse.Success(data));
		}

		/// <summary>
		/// Регистрация нового пользователя
		/// </summary>
		/// <param name="request">Данные о регистрации</param>
		/// <param name="authentication">Сервис аутентификации</param>
		/// <param name="billingService">Сервис биллинга</param>
		/// <param name="emailConfirmationService">Сервис подтверждения адреса email</param>
		/// <param name="cryptoProvider">Сервис криптографии</param>
		/// <returns>Булево значение об успешности регистрации</returns>
		[HttpPost]
		[AllowAnonymous]
		[ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
		public async Task<IActionResult> Post([FromBody] RegistrationRequest request,
			[FromServices] IAuthenticationManager authentication,
			[FromServices] RemoteBillingService billingService,
			[FromServices]IEmailConfirmationService emailConfirmationService,
			[FromServices]ICryptoProviderService cryptoProvider) {

			try {
				var value = Convert.FromBase64String(request.InitCms);
				var xml = new UTF8Encoding(false).GetString(value);
				var encrypted = XmlSerializationHelper.DeserializeFromXml<string>(xml);
				var decrypted = AesHelper.Decrypt(encrypted);
				var isValid = await cryptoProvider.VerifyCMSAsync(xml, request.SignedCms);
				if (!isValid) {
					return Json(ApiResponse.Failed(ApiErrorCode.ValidationError, "Сертификат не прошел проверку"));
				}
				var signUpDateTime = new DateTime(Convert.ToInt64(decrypted));
				if ((DateTime.Now - signUpDateTime).Hours > 0) {
					return Json(ApiResponse.Failed(ApiErrorCode.AuthenticationFailed, "С момента начала авторизации прошло больше часа"));
				}
			} catch {
				return Json(ApiResponse.Failed(ApiErrorCode.AuthenticationFailed, "Сбой дешифрации сообщения"));
			}
			await authentication.RegisterAsync(request, billingService);
			string code;
			long userId;
			if (!request.InvitedUser) {
				using (var repository = new Repository<User>(_provider)) {
					var user = repository.Get(x => x.UserName == request.UserAccount.Email).Single();
					code = emailConfirmationService.GenerateEmailConfirmationToken(user);
					repository.Update(user);
					repository.Commit();
					userId = user.Id;
				}

				var callbackUrl = Url.Action(
					"ConfirmEmail",
					"Registration",
					new { userId = userId, code = code },
					protocol: HttpContext.Request.Scheme);
				callbackUrl = callbackUrl.Replace("api/Registration/ConfirmEmail", "auth/confirmemail");
				emailConfirmationService.SendConfirmationUrl(request.UserAccount.Email, callbackUrl);
			}
			return Json(ApiResponse.Success(true));
		}

		[HttpPost("[action]")]
		[AllowAnonymous]
		public async Task<IActionResult> ConfirmEmail([FromBody]ConfirmEmailRequest confirmEmailRequest, [FromServices]IEmailConfirmationService emailConfirmationService) {
			using (var repository = new Repository<User>(_provider)) {
				var user = await repository.Get(x => x.Id == confirmEmailRequest.UserId).SingleAsync();
				if (user.Confirmed.HasValue) {
					return Json(ApiResponse.Failed(ApiErrorCode.ValidationError, "Данная регистрация была подтверждена ранее"));
				}
				var result = emailConfirmationService.ValidateConfirmationCode(user, confirmEmailRequest.Code);
				if (result) {
                    var notificationSettingsRep = new Repository<NotificationSettings>(repository);
                    var notificationSettings = new NotificationSettings {
                        DocumentReceived = true,
                        DocumentRejected = true,
                        DocumentRetired = true,
                        DocumentSend = true,
                        InviteSend = true,
                        ProfileAdd = true,
                        ProfileRemove = true,
                        DocumentSign=true,
                        User = user
                    };
                    await notificationSettingsRep.InsertAsync(notificationSettings);
					user.Confirmed = DateTime.Now;
					repository.Update(user);
					repository.Commit();
				}
				return Json(ApiResponse.Success(result));
			}
		}

		[HttpPut]
		[ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
		public async Task<IActionResult> Put([FromBody] UserAccountRequest request, [FromServices] IAuthenticationManager authentication) {
			if (request.Email != User.Identity.Name) {
				return Json(ApiResponse.Failed(ApiErrorCode.ValidationError, "У текущего пользователя недостаточно прав"));
			}
			await authentication.EditPasswordAsync(request);
			return Json(ApiResponse.Success(true));
		}

		[HttpPost("[action]")]
		[AllowAnonymous]
		[ProducesResponseType(200, Type = typeof(ApiResponse<string>))]
		public async Task<IActionResult> ForgotPassword([FromBody]ForgotPasswordRequest request, [FromServices]IEmailConfirmationService emailConfirmationService) {
			using (var repository = new Repository<User>(_provider)) {
				var user = await repository.Get(x => x.UserName == request.Email).SingleOrDefaultAsync();
				if (user == null) {
					return Json(ApiResponse.Success("Ссылка для восстановления пароля была выслана на указанный e-mail"));
				}
				var code = emailConfirmationService.GenerateEmailConfirmationToken(user);

				var callbackUrl = Url.Action(
					"ResetPassword",
					"Registration",
					new { userId = user.Id, code = code },
					protocol: HttpContext.Request.Scheme);
				callbackUrl = callbackUrl.Replace("api/Registration/ResetPassword", "auth/resetpassword");
				emailConfirmationService.SendForgotPasswordUrl(user.Email, callbackUrl);

				return Json(ApiResponse.Success("Ссылка для восстановления пароля была выслана на указанный e-mail"));
			}
		}

		[HttpPost("[action]")]
		[AllowAnonymous]
        [ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
		public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordRequest request,
			[FromServices] IAuthenticationManager authentication,
			[FromServices]IEmailConfirmationService emailConfirmationService) {
			using (var repository = new Repository<User>(_provider)) {
				var user = await repository.Get(x => x.Id == request.UserId).SingleOrDefaultAsync();
				if (user == null) {
					return Json(ApiResponse.Failed(ApiErrorCode.ValidationError, "Указаны неверные данные"));
				}
				var validateConfirmationResult = emailConfirmationService.ValidateConfirmationCode(user, request.Code);
				if (!validateConfirmationResult) {
					return Json(ApiResponse.Failed(ApiErrorCode.ValidationError, "Указаны неверные данные либо ссылка для восстановления уже использовалась ранее"));
				}
                var doPasswordMatch = await authentication.PasswordsMatched(user, request.Password);
                if (doPasswordMatch) {
                    return Json(ApiResponse.Failed(ApiErrorCode.ValidationError, "Старый пароль и новый совпадают"));
                }
			await authentication.EditPasswordAsync(new UserAccountRequest {
					Email = user.Email,
					Password = request.Password,
					PasswordConfirm = request.ConfirmPassword
				});
            
				return Json(ApiResponse.Success(true));
			}
		}

		[HttpPost("[action]")]
        [ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
		public async Task<ActionResult> ChangePassword([FromBody] ChangePasswordRequest request, [FromServices] IEmailService emailService,
            [FromServices] IAuthenticationManager authentication)
        {
            using (var repository = new Repository<User>(_provider)) {
                var user = await repository.Get(x => x.Email == User.Identity.Name).SingleOrDefaultAsync();
                var email = User.Identity.Name;
                var doPasswordMatch = await authentication.PasswordsMatched(user, request.oldPassword);
                if (!doPasswordMatch) {
                    return Json(ApiResponse.Failed(ApiErrorCode.ValidationError, "Текущий пароль неверный"));
                }

                if (request.oldPassword == request.Password) {
                    return Json(ApiResponse.Failed(ApiErrorCode.ValidationError, "Старый пароль и новый совпадают"));
                }
                await authentication.EditPasswordAsync(new UserAccountRequest {
                    Email = email,
                    Password = request.Password,
                    PasswordConfirm = request.ConfirmPassword
                });            
                emailService.SendEmail(email, "Смена пароля на Smartcontract.kz", "Вы успешно сменили пароль в системе Smartcontract.kz.");
                return Json(ApiResponse.Success(true));
            }
		}

        [HttpPost("[action]")]
        [ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
        public async Task<IActionResult> ChangePhone([FromBody] ChangePhoneRequest request, [FromServices] IEmailService emailService,
            [FromServices] IAuthenticationManager authentication) {
            var email = User.Identity.Name;
            await authentication.EditPhoneAsync(new UserAccountRequest {
                Email = email,
                Phone = request.Phone
            });
            emailService.SendEmail(email,"Изменение номера телефона на Smartcontract.kz","Вы успешно сменили номер телефона в системе Smartcontract.kz.");
            return Json(ApiResponse.Success(true));
        }
		[HttpPost("[action]")]
		[AllowAnonymous]
		[ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
		public async Task<ActionResult> GetCode([FromBody]PhoneNumberRequest request,
			[FromServices] IPhoneConfirmationService phoneConfirmation) {
			var canSendCode = await phoneConfirmation.SendConfirmationCodeAsync(request.PhoneNumber);
			return Json(ApiResponse.Success(canSendCode));
		}

		[HttpPost("[action]")]
		[AllowAnonymous]
		[ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
		public async Task<ActionResult> ValidateCode([FromBody]ConfirmationCodeRequest request,
			[FromServices] IPhoneConfirmationService phoneConfirmation) {
			var validated = await phoneConfirmation.ValidateConfirmationCodeAsync(request.PhoneNumber, request.Code);
			return Json(ApiResponse.Success(validated));
		}

        [HttpGet("[action]")]
        [ProducesResponseType(200, Type = typeof(ApiResponse<ClientInformation>))]
        public ActionResult ClientInformationByXin([FromQuery] string xin, [FromServices] IOrgnaizationsInformationService inviteService)
        {
            var clientInfo = inviteService.GetClientInformation(xin);
            return Json(ApiResponse.Success(clientInfo));
        }

        [HttpPost("[action]")]
		[ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
		public async Task<ActionResult> ContragentRegistration([FromBody] InviteRequest request,
			[FromServices] IInviteService inviteService)
        {
            var codeAndEmail = request.InviteClientRequest.Xin + ">" + HttpContext.User.Identity.Name;
            var code = inviteService.GenerateCode(codeAndEmail);
			var callbackUrl = Url.Action("GetInformationByInviteCode",
			"Registration",
			new { email = request.InviteClientRequest.Email, code },
			protocol: HttpContext.Request.Scheme);
			callbackUrl = callbackUrl.Replace("api/Registration/GetInformationByInviteCode", "auth/registration");
			var response = await inviteService.RegisterContrager(request, callbackUrl,User.Identity.Name);
			return Json(ApiResponse.Success(response));
		}

		[HttpPost("[action]")]
		[AllowAnonymous]
		[ProducesResponseType(200, Type = typeof(ApiResponse<ContragentInfoResponse>))]
		public async Task<IActionResult> GetInformationByInviteCode(IEmailService emailService, [FromBody] FinishContragentRegistrationRequest request, [FromServices] IInviteService inviteService, [FromServices] Provider provider) {
			var xinAndInviter = inviteService.DecodeCode(request.Code);
            var arrayXinAndInviter = xinAndInviter.Split('>');
            var xin = arrayXinAndInviter[0];
            var InviterEmail = arrayXinAndInviter[1];
			if (!string.IsNullOrEmpty(xin) && new XinAttribute().IsValid(xin)) {
				using (var repository = new Repository<Contragent>(provider)) {
					var contragent = repository.Get(u => u.Xin == xin).Select(u => new ContragentInfoResponse() {
						Xin = u.Xin,
						Name = u.FullName,
					}).SingleOrDefault();
					if (contragent != null) {
                        var settingsNotificationRep = new Repository<NotificationSettings>(repository);
                        var notificationSettingsSender = await settingsNotificationRep.Get(x => x.User.Email == InviterEmail).SingleAsync();
                        if (notificationSettingsSender.InviteSend) {
                            emailService.SendEmail(InviterEmail, "Приглашенный Вами пользователь зарегистрировался на Smartcontract.kz",
                                $"Приглашенный Вами пользователь {contragent.Name}, {contragent.Xin} зарегистрировался в системе Smartcontract.kz."+
                                "Теперь Вы можете обмениваться электронными документами.");
                        }                        
						return Json(ApiResponse.Success(contragent));
					}
				}
			}
			return Json(ApiResponse.Failed(ApiErrorCode.ValidationError, "Не верный код приглашения"));
		}
	}
}