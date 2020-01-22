using System;
using Common.DAL.Abstraction.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Smartcontract.Constants;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities;
using Smartcontract.DAL.Entities.References;

namespace Smartcontract.App.Managers {
	public class SampleData {
		private readonly Provider _provider;
		private readonly IHostingEnvironment _environment;
		private Random _random;

		public SampleData(Provider provider, IHostingEnvironment environment) {
			_provider = provider;
			_environment = environment;
			_random = new Random();
		}

		public void Fill() {
			using (var rep = new Repository<RefDocumentStatus>(_provider)) {
				if (rep.Any()) {
					return;
				}
				rep.Insert(new RefDocumentStatus { DocumentGroupType = DocumentGroupTypeEnum.Inbox, DocumentStatus = DocumentStatusEnum.OnSigning, Name = "На подписании", NameKz = "На подписании", NameEn = "На подписании", });
				rep.Insert(new RefDocumentStatus { DocumentGroupType = DocumentGroupTypeEnum.Inbox, DocumentStatus = DocumentStatusEnum.Signed, Name = "Подписанные", NameKz = "Подписанные", NameEn = "Подписанные", });
				rep.Insert(new RefDocumentStatus { DocumentGroupType = DocumentGroupTypeEnum.Inbox, DocumentStatus = DocumentStatusEnum.Rejected, Name = "Отклоненные", NameKz = "Отклоненные", NameEn = "Отклоненные", });
				rep.Insert(new RefDocumentStatus { DocumentGroupType = DocumentGroupTypeEnum.Inbox, DocumentStatus = DocumentStatusEnum.Retired, Name = "Отозванные", NameKz = "Отозванные", NameEn = "Отозванные", });

				rep.Insert(new RefDocumentStatus { DocumentGroupType = DocumentGroupTypeEnum.Outgoing, DocumentStatus = DocumentStatusEnum.OnSigning, Name = "На подписании", NameKz = "На подписании", NameEn = "На подписании", });
				rep.Insert(new RefDocumentStatus { DocumentGroupType = DocumentGroupTypeEnum.Outgoing, DocumentStatus = DocumentStatusEnum.Signed, Name = "Подписанные", NameKz = "Подписанные", NameEn = "Подписанные", });
				rep.Insert(new RefDocumentStatus { DocumentGroupType = DocumentGroupTypeEnum.Outgoing, DocumentStatus = DocumentStatusEnum.Rejected, Name = "Отклоненные", NameKz = "Отклоненные", NameEn = "Отклоненные", });
				rep.Insert(new RefDocumentStatus { DocumentGroupType = DocumentGroupTypeEnum.Outgoing, DocumentStatus = DocumentStatusEnum.Retired, Name = "Отозванные", NameKz = "Отозванные", NameEn = "Отозванные", });
				rep.Insert(new RefDocumentStatus { DocumentGroupType = DocumentGroupTypeEnum.Outgoing, DocumentStatus = DocumentStatusEnum.Drafts, Name = "Черновики", NameKz = "Черновики", NameEn = "Черновики", });


				var packetTypeRep = new Repository<PacketType>(rep);
				var types = new[] {
					new PacketType() {Code = "Y200", Name = "Пакет 200", Price=20_000},
					new PacketType() {Code = "Y500", Name = "Пакет 500", Price=45_000},
					new PacketType() {Code = "Y1000", Name = "Пакет 1000", Price=80_000},
					new PacketType() {Code = "Y2000", Name = "Пакет 2000", Price=145_000},
					new PacketType() { Code = "F10", Name = "Бонусный пакет", Price=0 }
				};
				foreach (var packetType in types) {
					packetTypeRep.Insert(packetType);
				}
				var activationCodeRep = new Repository<ActivationCode>(rep);
				

				for (int i = 0; i < 100; i++) {
					string code = "123456";
					if (!_environment.IsDevelopment()) {
						code = _random.Next(100000, 999999).ToString();
					}
					activationCodeRep.Insert(new ActivationCode() { Number = i.ToString("000000"), Code = code, GroupId = "SC", PacketType = types[i % 5] });
				}

				rep.Commit();
			}
		}
	}
}
