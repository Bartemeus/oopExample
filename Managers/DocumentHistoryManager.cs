using System;
using System.Linq;
using System.Threading.Tasks;
using Common.DAL.Abstraction.Repositories;
using Smartcontract.DataContracts.Document;
using Smartcontract.DAL;
using NHibernate.Linq;
using Smartcontract.DAL.Entities;

namespace Smartcontract.App.Managers {
	public class DocumentHistoryManager {
		private readonly Provider _provider;

		public DocumentHistoryManager(Provider provider) {
			_provider = provider;
		}
		public async Task WriteEntry(Document document, PersonProfile profile, string description, Repository baseRepository) {
			var repository = new Repository<DocumentHistoryEntry>(baseRepository);
			var entry = new DocumentHistoryEntry() {
				Document = document,
				Status = document.Status,
				ChangedBy = profile,
				Date = DateTime.Now,
				Description = description
			};
			await repository.InsertAsync(entry);
		}

		public async Task<HistoryEntryModel[]> GetHistoryAsync(string identityName, Guid personUniqueId, string documentUniqueId) {
			using (var repository = new Repository<DocumentHistoryEntry>(_provider)) {
				var query = repository.Get(x => x.Document.UniqueCode == documentUniqueId)
					.Select(x => new HistoryEntryModel {
						Date = x.Date,
						Description = x.Description,
						ChangedBy = x.ChangedBy.Contragent.FullName,
					});
				return query.ToArray();
			}
		}

		internal async Task WriteSignHistoryEntryAsync(Document document, PersonProfile currentProfile, string signature, Repository baseRepository) {
			var signHistoryRep = new Repository<SignHistoryItem>(baseRepository);
			var item = new SignHistoryItem() {
				Profile = currentProfile,
				Document = document,
				Signature = signature
			};
			await signHistoryRep.InsertAsync(item);
		}
	}


}