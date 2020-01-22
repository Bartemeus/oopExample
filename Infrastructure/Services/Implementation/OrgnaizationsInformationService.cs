using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common.DAL.Abstraction.Repositories;
using Smartcontract.App.Infrastructure.Services.Abstraction;
using Smartcontract.Constants;
using Smartcontract.DataContracts;
using Smartcontract.DataContracts.Registration;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities;
using Serilog;

namespace Smartcontract.App.Infrastructure.Services.Implementation {
	public class OrgnaizationsInformationService : IOrgnaizationsInformationService {
		private Provider _provider;
		public OrgnaizationsInformationService(Provider provider) {
			_provider = provider;

		}
		public ClientInformation GetClientInformation(string xin) {
			using (var rep = new Repository<Organization>(_provider)) {
				var clientInfo = rep.Get(u => u.Xin == xin).Select(u => new ClientInformation() {
					Xin = u.Xin,
					FullName = u.FullName,
					IsJuridic = u.IsJuridic,
					ContragentType = u.IsJuridic?ContragentTypeEnum.Legal:ContragentTypeEnum.IndividualEnterpreneur
				}).FirstOrDefault();
                if (clientInfo == null) {
                    Log.Error("{0}:{1}-xin='{2}'", "GetClientInformation", "clientInfo is null", xin);
					throw new SmartcontractException("Организация не найдена");

                }
                return clientInfo;
			}
		}
	}
}
