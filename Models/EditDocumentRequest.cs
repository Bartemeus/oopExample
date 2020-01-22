namespace Smartcontract.App.Models {
	public class EditDocumentRequest<TDetail> where TDetail : IDocumentDetailModel {
		public EditCardModel Card { get; set; }
		public TDetail Detail { get; set; }
	}
}
