using System;
using System.Collections.Generic;
using Smartcontract.Constants;

namespace Smartcontract.App.Managers.Models {
	[Serializable]
	public class DocumentContainer {
		public string UniqueCode { get; set; }
		public string SenderPersonIin { get; set; }
		public string SenderContragentXin { get; set; }
		public string RecipientContragentXin { get; set; }
		public DateTime RegistrationDate { get; set; }
		public DateTime CreationDate { get; set; }
		public string InternalNumber { get; set; }
		public string BasementDocumentId { get; set; }
		public string Description { get; set; }
		public byte	 DocumentType { get; set; }
		public List<DocumentFile> Files { get; set; }

		public DocumentContainer() {
			CreationDate = new DateTime(DateTime.Now.Ticks, DateTimeKind.Unspecified);
			Files = new List<DocumentFile>();
		}


		public DocumentContainer(string senderPersonIin, string senderContragentXin, string recipientContragentXin) : this() {
			SenderPersonIin = senderPersonIin;
			SenderContragentXin = senderContragentXin;
			this.RecipientContragentXin = recipientContragentXin;
		}

		public void LoadCommon(string internalNumber, DateTime registrationDate, string description) {
			this.Description = description;
			this.RegistrationDate = registrationDate;
			this.InternalNumber = internalNumber;
		}


		public void AddFileInfo(string fileName, string contentType, int size, string hash) {
			Files.Add(new DocumentFile(fileName, contentType, size, hash));
		}
	}
}