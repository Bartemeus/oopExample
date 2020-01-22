using Smartcontract.App.Infrastructure.Services.Abstraction;
using System;
using System.Linq;
using System.Threading.Tasks;
using Common.DAL.Abstraction.Repositories;
using Serilog;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities;

namespace Smartcontract.App.Infrastructure.Services.Implementation {
	public class PhoneConfirmationService : IPhoneConfirmationService {
		public const string EventId = "SMS";
		private readonly Provider _provider;
		private readonly ISmsService _smsService;
		private readonly INumbersGenerator _numbersGenerator;
		public TimeSpan CodeValidPeriod = TimeSpan.FromMinutes(5);
		private int _codeLength = 5;
		public PhoneConfirmationService(Provider provider, INumbersGenerator numbersGenerator, ISmsService smsService) {
			this._provider = provider;
			this._smsService = smsService;
			this._numbersGenerator = numbersGenerator;
		}
		

		private bool CanSendAnotherConfirmationCode(Phone phone) {            
			if (phone == null) {
				return true;
			}
			if ((phone.Sended.Add(this.CodeValidPeriod) < DateTime.Now)) {
				return true;
			}
			Log.Warning("{EventId} {phone} code can't be sent", EventId, phone.PhoneNumber);
			return false;
		}

		public async Task<bool> SendConfirmationCodeAsync(string phoneNumber) {
			using (var phoneRep = new Repository<Phone>(_provider)) {                
                var phone = phoneRep.Get(u => u.PhoneNumber == phoneNumber).OrderByDescending(x => x.Id).FirstOrDefault();                
                var canSendCode = CanSendAnotherConfirmationCode(phone);                
                if (canSendCode) {					
                    phone = new Phone { PhoneNumber = phoneNumber, Sended = DateTime.Now, Code = _numbersGenerator.GenerateNumbers(_codeLength) };
					Log.Information("{EventId} {phone} {code}", EventId, phoneNumber, phone.Code);
                    phoneRep.Insert(phone);
                    phoneRep.Commit();
					await _smsService.SendAsync(phone.PhoneNumber, $"{phone.Code} для SmartContract.kz");
				}
				return canSendCode;
			}
		}
		public async Task<bool> ValidateConfirmationCodeAsync(string phoneNumber, string smsCode) {
			using (var phoneRep = new Repository<Phone>(_provider)) {
				var phone = phoneRep.Get(u => u.PhoneNumber == phoneNumber).OrderByDescending(x => x.Id).FirstOrDefault();
                if (phone != null) {                   
                    if (phone.Sended.Add(this.CodeValidPeriod) >= DateTime.Now && phone.Code == smsCode) {                        
                        phone.Confirmed = DateTime.Now;
						phoneRep.Update(phone);
						phoneRep.Commit();
						return true;
					}
				}
			    Log.Warning("{EventId} {phoneNumber} code can't be Validate", EventId, phoneNumber);
                return false;
			}
		}
	}

}
