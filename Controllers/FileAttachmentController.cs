using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Common.DAL.Abstraction.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver.GridFS;
using Smartcontract.App.Managers;
using Smartcontract.Common;
using Common.DataContracts.Base;
using Smartcontract.DataContracts.FileAttachment;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities;
using Smartcontract.DAL.FileStorage;

namespace Smartcontract.App.Controllers {
	[Route("api/[controller]")]
	[ApiController]
	[Authorize]
	public class FileAttachmentController : Controller {
		private Provider _provider;
		private readonly DocumentHistoryManager _historyManager;

		public FileAttachmentController(Provider provider, DocumentHistoryManager historyManager) {
			_provider = provider;
			_historyManager = historyManager;
		}
		[HttpPost]
		[ProducesResponseType(200, Type = typeof(ApiResponse<FileAttachmentResponse>))]
		[RequestSizeLimit(60 * 1024 * 1024)]
		public async Task<IActionResult> Post(IFormFile attachmentFile, [FromServices]FileStorageProvider fileStorageProvider) {
			var bucket = fileStorageProvider.GetDocumentBucket();
			var fileStream = new MemoryStream();
			await attachmentFile.CopyToAsync(fileStream);
			var metadata = new AttachmentFileMetadata {
				FileName = attachmentFile.FileName,
				ContentType = attachmentFile.ContentType,
				Size = fileStream.Length,
				Uploaded = DateTime.Now,
				ContentHash = Helper.CalculateSha1Hash(fileStream.ToArray())
			};
            fileStream.Position = 0;
			var id = await bucket.UploadFromStreamAsync(attachmentFile.FileName, fileStream, new GridFSUploadOptions() {
				Metadata = metadata.ToBsonDocument()
			});
			var result = new FileAttachmentResponse() { FileName = attachmentFile.FileName, FileId = id.ToString() };
			return Json(ApiResponse.Success(result));
		}

		[HttpPost("[action]")]
		[ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
		public async Task<IActionResult> RemoveAttachments([FromBody]FileAttachmentResponse[] attachments, [FromServices]FileStorageProvider fileStorageProvider) {
			using (var attachmentsRep = new Repository<DocumentAttachment>(_provider)) {
				var fileIds = attachments.Select(x => x.FileId).ToArray();
				var savedAttachmentCount = attachmentsRep.Get(x => fileIds.Contains(x.StorageId)).Count();
				if (savedAttachmentCount > 0) {
					return Json(ApiResponse.Failed(ApiErrorCode.ValidationError, "Невозможно удалить файлы привязанные к документу"));
				}
			}

			foreach (var attachment in attachments) {
				var bucket = fileStorageProvider.GetDocumentBucket();
				await bucket.DeleteAsync(new ObjectId(attachment.FileId));
			}
			return Json(ApiResponse.Success(true));
		}

		[HttpPost("[action]")]
		[ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
		[RequestSizeLimit(60 * 1024 * 1024)]
		public async Task<IActionResult> AddFileToDocument(IFormFile attachmentFile, [FromHeader]Guid personUniqueId, [FromQuery]string uniqueCode, 
			[FromServices]FileStorageProvider fileStorageProvider, [FromServices] DocumentsManager manager) {
			var existAttachmentFile = await manager.CheckExistAttachmentFile(User.Identity.Name, uniqueCode, personUniqueId, attachmentFile);
			if (existAttachmentFile) {
				return Json(ApiResponse.Success(false));
			}
			var bucket = fileStorageProvider.GetDocumentBucket();
			var fileStream = new MemoryStream();
			await attachmentFile.CopyToAsync(fileStream);
			var metadata = new AttachmentFileMetadata {
				FileName = attachmentFile.FileName,
				ContentType = attachmentFile.ContentType,
				Size = fileStream.Length,
				Uploaded = DateTime.Now,
				ContentHash = Helper.CalculateSha1Hash(fileStream.ToArray())
			};
			fileStream.Position=0;
			var id = await bucket.UploadFromStreamAsync(attachmentFile.FileName, fileStream, new GridFSUploadOptions() {
				Metadata = metadata.ToBsonDocument()
			});

			var result = await manager.AddAttachmentFileToDraft(User.Identity.Name, uniqueCode, personUniqueId,new FileAttachmentResponse { FileId = id.ToString(),FileName = attachmentFile.FileName} );
			return Json(ApiResponse.Success(result));
		}

		[HttpDelete]
		[ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
		public async Task<IActionResult> Delete(
		[FromServices] DocumentsManager manager, 
		[FromServices]FileStorageProvider fileStorageProvider,
		[FromForm]long key, 
		[FromHeader]Guid personUniqueId, 
		[FromQuery] string uniqueCode) {
			using (var attachmentsRep = new Repository<DocumentAttachment>(_provider)) {
				var documentRep = new Repository<Document>(attachmentsRep);
				var document = documentRep.Get(x=>x.UniqueCode == uniqueCode && x.SenderPerson.UniqueId == personUniqueId).FirstOrDefault();
				if(document == null){
					return Json(ApiResponse.Failed(ApiErrorCode.ValidationError, "Невозможно удалить файлы не привязанные к документу"));
				}
				var savedAttachment = attachmentsRep.Get(x => x.Id == key && x.Document.Id == document.Id).FirstOrDefault();
				if (savedAttachment == null) {
					return Json(ApiResponse.Failed(ApiErrorCode.ValidationError, "Невозможно удалить файлы не привязанные к документу"));
				}
				await attachmentsRep.DeleteAsync(savedAttachment);
				await _historyManager.WriteEntry(document, document.SenderPerson, $"Файл: {savedAttachment.FileName} - удален", attachmentsRep);	
				await attachmentsRep.CommitAsync();

				var bucket = fileStorageProvider.GetDocumentBucket();
				await bucket.DeleteAsync(new ObjectId(savedAttachment.StorageId));
			}		
			return Json(ApiResponse.Success(true));
		}
	}
}