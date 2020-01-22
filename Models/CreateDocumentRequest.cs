namespace Smartcontract.App.Models {
	public class CreateDocumentRequest<TDetail> where TDetail : IDocumentDetailModel {
		public CreateCardModel Card { get; set; }
		public TDetail Detail { get; set; }
	}
}
