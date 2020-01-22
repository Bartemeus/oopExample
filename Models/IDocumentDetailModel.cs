using Common.DAL.Abstraction.Repositories;
using Smartcontract.DataContracts.FileAttachment;

namespace Smartcontract.App.Models {
	public interface IDocumentDetailModel {
		FileAttachmentResponse[] Create(Repository repository);
	}
}