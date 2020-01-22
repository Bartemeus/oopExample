using System;

namespace Smartcontract.App.Managers.Models {
	[Serializable]
	public class DocumentFile {
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public int Size { get; set; }
		public string ContentHash { get; set; }

		public DocumentFile() {

		}

		public DocumentFile(string fileName, string contentType, int size, string hash) {
			FileName = fileName;
			ContentType = contentType;
			Size = size;
			ContentHash = hash;
		}
	}
}