using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Common.DAL.Abstraction.Repositories;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Data.ResponseModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Smartcontract.App.Infrastructure.DevExtreme;
using Smartcontract.App.Infrastructure.Services.Abstraction;
using Smartcontract.App.Managers;
using Smartcontract.App.Models;
using Smartcontract.Constants;
using Smartcontract.DataContracts.Document;
using Smartcontract.DataContracts.FileAttachment;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities;
using Common.DataContracts.Base;


namespace Smartcontract.App.Controllers {
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DocumentController : Controller {
        private readonly Provider _provider;

        public DocumentController(Provider provider) {
            _provider = provider;
        }

        [HttpGet]
        public async Task<IActionResult> Get(
            [FromQuery] DocumentGroupTypeEnum documentGroupType, [FromQuery] DocumentStatusEnum documentStatus,
            [FromHeader]Guid personUniqueId,
            [FromQuery] DataSourceLoadOptionsImpl options,
            [FromServices] DocumentsManager manager) {
            using (var repository = new Repository<Document>(_provider)) {
                var query = await manager.GetMyDocumentsAsync(User.Identity.Name, personUniqueId, documentGroupType, documentStatus, repository);
                return this.JsonEx(DataSourceLoader.Load(query, options));
            }
        }

        [HttpGet("[action]")]
        public async Task<IActionResult> GetBasement(
            [FromQuery] DocumentGroupTypeEnum documentGroupType,
            [FromHeader]Guid personUniqueId,
            [FromQuery] DataSourceLoadOptionsImpl options,
            [FromServices] DocumentsManager manager) {
            using (var repository = new Repository<Document>(_provider)) {
                var query = await manager.GetMyDocumentsAsync(User.Identity.Name, personUniqueId, documentGroupType, DocumentStatusEnum.Signed,
                    repository);
                return this.JsonEx(DataSourceLoader.Load(query, options));
            }
        }

        [HttpPost("[action]")]
        [ProducesResponseType(200, Type = typeof(ApiResponse<string>))]
        public async Task<IActionResult> CreateOther(
            [FromHeader]Guid personUniqueId,
            [FromBody] CreateDocumentRequest<OtherDocumentDetail> model,
            [FromServices] DocumentsManager manager) {
            var documentId = await manager.CreateDocumentAsync(User.Identity.Name, personUniqueId, model);
            return Json(new ApiResponse<string>(documentId));
        }

        [HttpPost("[action]")]
        [ProducesResponseType(200, Type = typeof(ApiResponse<string>))]
        public async Task<IActionResult> EditOther(
            [FromHeader]Guid personUniqueId,
            [FromQuery] string uniqueId,
            [FromBody] EditDocumentRequest<OtherDocumentDetail> model,
            [FromServices] DocumentsManager manager) {
            var documentId = await manager.EditDocumentAsync(User.Identity.Name, personUniqueId, uniqueId, model);
            return Json(new ApiResponse<string>(documentId));
        }

        [HttpGet("[action]")]
        [ProducesResponseType(200, Type = typeof(ApiResponse<string>))]
        public async Task<IActionResult> GetXml(
            [FromHeader]Guid personUniqueId,
            [FromQuery] string documentUniqueId,
            [FromServices] DocumentsManager manager) {
            var xml = await manager.GenerateXmlAsync(User.Identity.Name, documentUniqueId);
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(xml.Data));
            return Json(ApiResponse.Success(base64));
        }

		[HttpPost("[action]")]
		[ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
		public async Task<IActionResult> SendToSign(
			[FromHeader]Guid personUniqueId,
			[FromQuery] string documentUniqueId,
			[FromBody] SignedDraftModel model,
			[FromServices] DocumentsManager manager, [FromServices] ICryptoProviderService cryptoProvider) {           
            await manager.SignDocumentAsync(User.Identity.Name, personUniqueId, documentUniqueId, model.Signature, cryptoProvider);
			return Json(new ApiResponse<bool>(true));
		}

		[HttpPost("[action]")]
		[ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
		public async Task<IActionResult> Rejected(
			[FromHeader]Guid personUniqueId,
            [FromQuery] string documentUniqueId,
            [FromBody] DocumentRejectedRequest request,
			[FromServices] DocumentsManager manager) {                            
            await manager.RejectDocumentAsync(User.Identity.Name, personUniqueId,request);
			return Json(new ApiResponse<bool>(true));
		}

        [HttpPost("[action]")]
        [ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
        public async Task<IActionResult> Retired(
            [FromHeader]Guid personUniqueId,
            [FromQuery] string documentUniqueId,
            [FromServices] DocumentsManager manager) {
            await manager.RetireDocumentAsync(User.Identity.Name, personUniqueId, documentUniqueId);
            return Json(new ApiResponse<bool>(true));
        }

        [HttpGet("[action]")]
        [ProducesResponseType(200, Type = typeof(ApiResponse<HistoryEntryModel[]>))]
        public async Task<IActionResult> History(
            [FromHeader]Guid personUniqueId,
            [FromQuery] string documentUniqueId,
            [FromServices] DocumentHistoryManager manager) {
            var history = await manager.GetHistoryAsync(User.Identity.Name, personUniqueId, documentUniqueId);
            return Json(ApiResponse.Success(history));
        }

        [HttpGet("[action]")]
        [ProducesResponseType(200, Type = typeof(ApiResponse<DocumentAttachmentViewModel[]>))]
        public async Task<IActionResult> Attachments(
            [FromHeader]Guid personUniqueId,
            [FromQuery] string documentUniqueId,
            [FromServices] DocumentsManager manager) {
            var attachments = await manager.GetAttachmentsAsync(User.Identity.Name, personUniqueId, documentUniqueId);
            return Json(ApiResponse.Success(attachments));
        }

        [HttpGet("[action]")]
        [ProducesResponseType(200, Type = typeof(ApiResponse<DocumentCardModel>))]
        public async Task<IActionResult> Card(
            [FromHeader]Guid personUniqueId,
            [FromQuery] string documentUniqueId,
            [FromServices] DocumentsManager manager) {
            var card = await manager.GetDocumentCardAsync(User.Identity.Name, personUniqueId, documentUniqueId);
            if (card == null) {
                return Json(ApiResponse.Failed(ApiErrorCode.ResourceNotFound, $"Документ с номером {documentUniqueId} не найден"));
            }
            return Json(ApiResponse.Success(card));
        }

        [HttpGet("[action]")]
        public async Task<IActionResult> DownloadPackage(
            [FromQuery] Guid personUniqueId,
            [FromQuery] string documentUniqueId,
            [FromServices] DocumentsManager manager)
        {
            var packageBytes = await manager.CreatePackageAsync(User.Identity.Name, personUniqueId, documentUniqueId);
            return File(packageBytes, "application/zip", $"{documentUniqueId}.zip");
        }

        [HttpGet("[action]")]
        public async Task<IActionResult> DownloadFile(
            [FromQuery] Guid personUniqueId,
            [FromQuery] string uniqueCode,
            [FromQuery] long fileId,
            [FromServices] DocumentsManager manager)
        {
            var customFile = await manager.GetFileAsync(User.Identity.Name, personUniqueId, uniqueCode, fileId);
            var ms = new MemoryStream(customFile.FileContents);
            return File(ms, customFile.ContentType, customFile.FileName);
        }

        [HttpPost("[action]")]
		[ProducesResponseType(200, Type = typeof(ApiResponse<DocumentValidationModel>))]
		[AllowAnonymous]
		public async Task<IActionResult> UploadAndValidatePackage(
			[FromServices] ICryptoProviderService cryptoProvider,
			[FromServices] DocumentsManager manager,
			IFormFile attachmentFile) {
			var result = await manager.UploadAndValidatePackageAsync(cryptoProvider, attachmentFile);
			return Json(ApiResponse.Success(result));
		}
	}
}