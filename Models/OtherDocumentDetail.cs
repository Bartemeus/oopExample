using Common.DAL.Abstraction.Repositories;
using Smartcontract.DataContracts.FileAttachment;

namespace Smartcontract.App.Models {
	public class OtherDocumentDetail : IDocumentDetailModel {
		public FileAttachmentResponse[] Files { get; set; }

		public FileAttachmentResponse[] Create(Repository repository) {
			return Files;
		}
	}
}
