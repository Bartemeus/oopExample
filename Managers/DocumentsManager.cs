using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Common.DAL.Abstraction.Repositories;
using Ionic.Zip;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using NHibernate.Linq;
using Smartcontract.App.Infrastructure.Services.Abstraction;
using Smartcontract.App.Infrastructure.Services.Implementation;
using Smartcontract.App.Managers.Models;
using Smartcontract.App.Models;
using Smartcontract.Common;
using Smartcontract.Common.Certificates;
using Smartcontract.Constants;
using Smartcontract.DataContracts.Document;
using Smartcontract.DataContracts.FileAttachment;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities;
using Smartcontract.DAL.FileStorage;
using Common.DataContracts.Base;
using FluentNHibernate.Conventions;
using Microsoft.AspNetCore.Mvc;
using Smartcontract.Constants.Enums;
using Smartcontract.DataContracts;
using QRCoder;


namespace Smartcontract.App.Managers {
	public class DocumentsManager {
		private Provider _provider;
		private readonly FileStorageProvider _fileStorageProvider;
		private readonly DocumentHistoryManager _historyManager;
		private readonly RemoteBillingService _billingService;
        private readonly IEmailService _emailService;
        private readonly HttpRequest _request;
		public DocumentsManager(Provider provider,
			FileStorageProvider fileStorageProvider,
			DocumentHistoryManager historyManager,
			RemoteBillingService billingService,
            IEmailService emailService,
            IHttpContextAccessor httpContextAccessor) {
			_provider = provider;
			_fileStorageProvider = fileStorageProvider;
			_historyManager = historyManager;
			_billingService = billingService;
            _emailService = emailService;
            _request = httpContextAccessor.HttpContext.Request;
        }

		public async Task<IQueryable<DocumentResponse>> GetMyDocumentsAsync(string userName, Guid personUniqueId,
			DocumentGroupTypeEnum documentGroupType,
			DocumentStatusEnum documentStatus, Repository baseRepository) {
			var documentRepository = new Repository<Document>(baseRepository);
			var userRepository = new Repository<User>(documentRepository);
			var userId = await userRepository.Get(x => x.UserName == userName).Select(x => x.Id).SingleAsync();
			var contragentId = userRepository.Get(x => x.Id == userId)
				.SelectMany(x => x.PersonProfiles)
				.Where(x => x.UniqueId == personUniqueId)
				.Select(x => x.Contragent)
				.Select((x => (long?)x.Id)).FirstOrDefault();
			if (!contragentId.HasValue) {
				throw new SmartcontractException("Профиль не найден", ApiErrorCode.ValidationError);
			}

			Expression<Func<Document, bool>> queryExpression;
			switch (documentGroupType) {
				case DocumentGroupTypeEnum.Inbox:
					queryExpression = x => x.Status == documentStatus && x.RecipientContragent.Id == contragentId.Value;
					break;
				case DocumentGroupTypeEnum.Outgoing:
					queryExpression = x => x.Status == documentStatus && x.SenderPerson.Contragent.Id == contragentId.Value;
					break;
				default:
					throw new NotImplementedException();
			}

			var query = documentRepository.Get(queryExpression).Select(x => new DocumentResponse {
				UniqueCode = x.UniqueCode,
				InternalNumber = x.Number,
				RegistrationDate = x.RegistrationDate,
				Summary = x.Summary,
				DocumentType = x.DocumentType,
				Status = x.Status,
				SenderPersonName = x.SenderPerson.FullName,
				SenderContragentName = x.SenderPerson.Contragent.FullName,
				SenderContragentXin = x.SenderPerson.Contragent.Xin,
				RecipientContragentName = x.RecipientContragent.FullName,
				RecipientContragentXin = x.RecipientContragent.Xin,
				IsNotOpened = x.FirstOpened == null,
			});
			return query;
		}

		public async Task<bool> AddAttachmentFileToDraft(string userName, string documentUniqueId, Guid personUniqueId,
			FileAttachmentResponse fileAttachment) {
			using (var userRep = new Repository<User>(_provider)) {
				var documentsRep = new Repository<Document>(userRep);
				var attachmentsRep = new Repository<DocumentAttachment>(userRep);
				var profileManager = new ProfileManager(_provider);

				var sender = await profileManager.GetPersonProfileQuery(userName, personUniqueId, userRep).SingleOrDefaultAsync();
				if (sender == null) {
					return false;
				}

				var document = documentsRep.Get(x => x.UniqueCode == documentUniqueId && x.SenderPerson.Id == sender.Id).FirstOrDefault();
				if (document == null || document.Status != DocumentStatusEnum.Drafts) {
					return false;
				}

				var attachments = await GetAttachmentsAsync(new[] { fileAttachment }, document);
				if (attachments == null || !attachments.Any()) {
					return false;
				}

				await attachmentsRep.InsertAsync(attachments);
				foreach (var documentAttachment in attachments) {
					await _historyManager.WriteEntry(document, sender, $"К черновику добавлен файл: {documentAttachment.FileName}", userRep);
				}

				await documentsRep.CommitAsync();
				return true;
			}
		}

		public async Task<bool> CheckExistAttachmentFile(string userName, string documentUniqueId, Guid personUniqueId, IFormFile attachmentFile) {
			using (var documentsRep = new Repository<Document>(_provider)) {
				var attachmentsRep = new Repository<DocumentAttachment>(documentsRep);
				var profileManager = new ProfileManager(_provider);

				var senderId = await profileManager.GetPersonProfileQuery(userName, personUniqueId, documentsRep)
					.Select(x => (long?)x.Id)
					.SingleOrDefaultAsync();
				if (!senderId.HasValue) {
					return false;
				}

				var document = documentsRep.Get(x => x.UniqueCode == documentUniqueId && x.SenderPerson.Id == senderId.Value)
					.Select(x => new { x.Id, x.Status }).FirstOrDefault();
				if (document == null || document.Status != DocumentStatusEnum.Drafts) {
					return false;
				}

				var attachment = attachmentsRep.Get(x => x.Document.Id == document.Id && x.FileName == attachmentFile.FileName).FirstOrDefault();
				return attachment != null;
			}
		}

		public async Task<string> CreateDocumentAsync<TDetail>(string userName, Guid personUniqueId, CreateDocumentRequest<TDetail> model)
			where TDetail : IDocumentDetailModel {
			if (model.Card.RegistrationDate > DateTime.Now) {
				throw new SmartcontractException("Дата регистрации документа указана неверно", ApiErrorCode.ValidationError);
			}

			using (var contragentRep = new Repository<Contragent>(_provider)) {
				var documentsRep = new Repository<Document>(contragentRep);
				var attachmentsRep = new Repository<DocumentAttachment>(contragentRep);
				var currentProfile = await GetCurrentPerson(userName, personUniqueId, contragentRep);
				var receiver = await contragentRep.Get(x => x.UniqueId == model.Card.RecipientUniqueId)
					.SingleAsync();
				var document = new Document() {
					Status = DocumentStatusEnum.Drafts,
					SenderPerson = currentProfile,
					RecipientContragent = receiver,
					RegistrationDate = model.Card.RegistrationDate,
					Number = model.Card.InternalNumber,
					UniqueCode = await GenerateUniqueCodeAsync(documentsRep),
					Summary = model.Card.Summary
				};


				if (!string.IsNullOrEmpty(model.Card.BasementDocumentId)) {
					var parent = await documentsRep.Get(x => x.UniqueCode == model.Card.BasementDocumentId).SingleOrDefaultAsync();
					document.ParentDocument = parent;
				}

				var attachments = await GetAttachmentsAsync(model.Detail.Create(contragentRep), document);
				if (attachments.GroupBy(x => x.FileName).Any(x => x.Count() > 1)) {
					throw new SmartcontractException("Нельзя прикладывать файлы с одинаковыми именами", ApiErrorCode.ValidationError);
				}
				document.RawXml = GenerateSignature(currentProfile, receiver, document, attachments);
				await documentsRep.InsertAsync(document);
				await attachmentsRep.InsertAsync(attachments);
				await _historyManager.WriteEntry(document, currentProfile, "Документ создан", contragentRep);
				await IncrementDocumentsSummaryAsync(document.SenderPerson.Contragent, DocumentStatusEnum.Drafts, DocumentGroupTypeEnum.Outgoing,
					documentsRep);
				await documentsRep.CommitAsync();
				return document.UniqueCode;
			}
		}


		public async Task<string> EditDocumentAsync<TDetail>(string userName, Guid personUniqueId, string uniqueId,
			EditDocumentRequest<TDetail> model)
			where TDetail : IDocumentDetailModel {
			if (model.Card.RegistrationDate > DateTime.Now) {
				throw new SmartcontractException("Дата регистрации документа указана неверно", ApiErrorCode.ValidationError);
			}
			using (var contragentRep = new Repository<Contragent>(_provider)) {
				var documentsRep = new Repository<Document>(contragentRep);

				var currentProfile = await GetCurrentPerson(userName, personUniqueId, contragentRep);
				;
				var receiver = await contragentRep.Get(x => x.UniqueId == model.Card.RecipientUniqueId)
					.SingleAsync();

				var document = await documentsRep.Get(x => x.UniqueCode == uniqueId && x.SenderPerson.Id == currentProfile.Id).SingleOrDefaultAsync();
				document.RecipientContragent = receiver;
				document.RegistrationDate = model.Card.RegistrationDate;
				document.Number = model.Card.InternalNumber;
				document.Summary = model.Card.Summary;
				document.RawXml = await GenerateSignatureAsync(contragentRep, currentProfile, receiver, document);
				await documentsRep.UpdateAsync(document);
				await _historyManager.WriteEntry(document, currentProfile, "Документ отредактирован", contragentRep);
				await documentsRep.CommitAsync();
				return document.UniqueCode;
			}
		}

		private async Task<string> GenerateSignatureAsync(Repository repository, PersonProfile sender, Contragent recipient, Document document) {
			var documentAttachmentsRep = new Repository<DocumentAttachment>(repository);
			var attachments = await documentAttachmentsRep.Get(x => x.Document.Id == document.Id).ToListAsync();
			return GenerateSignature(sender, recipient, document, attachments);
		}

		private string GenerateSignature(PersonProfile sender, Contragent recipient, Document document, IEnumerable<DocumentAttachment> attachments) {
			var documentContainer = new DocumentContainer(sender.Iin, sender.Contragent.Xin, recipient.Xin) {
				UniqueCode = document.UniqueCode,
				DocumentType = (byte)document.DocumentType,
				BasementDocumentId = document.ParentDocument?.UniqueCode
			};
			documentContainer.LoadCommon(document.Number, document.RegistrationDate, document.Summary);
			foreach (var attachment in attachments) {
				documentContainer.AddFileInfo(attachment.FileName, attachment.ContentType, attachment.Size, attachment.ContentHash);
			}

			var xml = SerializeToXml(documentContainer);
			return xml;
		}

		private async Task IncrementDocumentsSummaryAsync(Contragent contragent, DocumentStatusEnum status, DocumentGroupTypeEnum group,
			Repository baseRepository) {
			var documentsSummaryRep = new Repository<DocumentsSummary>(baseRepository);
			var summary = await documentsSummaryRep.Get(x => x.Contragent.Id == contragent.Id && x.Status == status && x.DocumentGroupType == group)
				.SingleOrDefaultAsync();
			if (summary == null) {
				summary = new DocumentsSummary() {
					DocumentGroupType = group,
					Contragent = contragent,
					Status = status,
					DocumentsCount = 0,
					NewDocumentsCount = 0
				};
				await documentsSummaryRep.InsertAsync(summary);
			}

			summary.DocumentsCount++;
			await documentsSummaryRep.UpdateAsync(summary);
		}

		private async Task DecrementDocumentsSummaryAsync(Contragent contragent, DocumentStatusEnum documentStatus, DocumentGroupTypeEnum group,
			Repository baseRepository) {
			var documentsSummaryRep = new Repository<DocumentsSummary>(baseRepository);
			var summary = await documentsSummaryRep
				.Get(x => x.Contragent.Id == contragent.Id && x.Status == documentStatus && x.DocumentGroupType == group)
				.SingleAsync();
			summary.DocumentsCount--;
			await documentsSummaryRep.UpdateAsync(summary);
		}

		private async Task<IEnumerable<DocumentAttachment>> StoreAttachmentsAsync(IFormFileCollection modelAttachments, Document document) {
			var attachments = new List<DocumentAttachment>();
			if (modelAttachments == null) {
				return attachments;
			}

			var bucket = _fileStorageProvider.GetDocumentBucket();
			foreach (var uploadedFile in modelAttachments) {
				var ms = new MemoryStream();
				await uploadedFile.CopyToAsync(ms);
				var id = await bucket.UploadFromStreamAsync(uploadedFile.FileName, ms);
				attachments.Add(new DocumentAttachment() {
					Document = document,
					ContentType = uploadedFile.ContentType,
					FileName = uploadedFile.FileName,
					Size = (int)ms.Length,
					StorageId = id.ToString(),
					ContentHash = Helper.CalculateSha1Hash(ms.ToArray())
				});
			}

			return attachments;
		}

		private async Task<IEnumerable<DocumentAttachment>> GetAttachmentsAsync(FileAttachmentResponse[] modelAttachments, Document document) {
			var attachments = new List<DocumentAttachment>();
			if (modelAttachments == null) {
				return attachments;
			}

			var bucket = _fileStorageProvider.GetDocumentBucket();
			foreach (var uploadedFile in modelAttachments) {
				var fileInfoQuery =
					await bucket.FindAsync(new ExpressionFilterDefinition<GridFSFileInfo<ObjectId>>(x => x.Id == new ObjectId(uploadedFile.FileId)));
				var fileInfo = await fileInfoQuery.FirstAsync();

				AttachmentFileMetadata metadata = null;
				if (fileInfo.Metadata != null) {
					metadata = BsonSerializer.Deserialize<AttachmentFileMetadata>(fileInfo.Metadata);
				}

				attachments.Add(new DocumentAttachment() {
					Document = document,
					ContentType = metadata?.ContentType,
					FileName = uploadedFile.FileName,
					Size = metadata != null ? (int)metadata.Size : 0,
					StorageId = uploadedFile.FileId,
					ContentHash = metadata?.ContentHash
				});
			}

			return attachments;
		}

		private async Task<string> GenerateUniqueCodeAsync(Repository<Document> documentsRep) {
			var today = DateTime.Today;
			var yesterday = today.AddDays(1);
			var countToday = await documentsRep.Get(x => x.SystemRegistrationDate >= today && x.SystemRegistrationDate < yesterday).CountAsync();
			return string.Format("SC-{0:yyyyddMM}{1:0000}", today, countToday + 1);
		}

		public async Task<ApiResponse<string>> GenerateXmlAsync(string userName, string documentUniqueId) {
			if (!await _billingService.IsAvailableAsync(userName)) {
				return ApiResponse.Failed<string>(ApiErrorCode.DoesntHaveActivePacket, "Нет доступных активационных пакетов");
			}

			using (var documentsRep = new Repository<Document>(_provider)) {
				var signature = await documentsRep.Get(x => x.UniqueCode == documentUniqueId)
					.Select(x => x.RawXml)
					.SingleOrDefaultAsync();

				return ApiResponse.Success(signature);
			}
		}

		private string SerializeToXml<T>(T documentContainer, bool removeBOM = true) {
			using (var memoryStream = new MemoryStream()) {
				using (var streamWriter = new StreamWriter(memoryStream, System.Text.Encoding.UTF8)) {
					var serializer = new XmlSerializer(typeof(T));
					serializer.Serialize(streamWriter, documentContainer);
				}

				var xml = Encoding.UTF8.GetString(memoryStream.ToArray());
				if (removeBOM) {
					var byteOrderMarkUtf8 = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
					if (xml.StartsWith(byteOrderMarkUtf8, StringComparison.Ordinal)) {
						xml = xml.Remove(0, byteOrderMarkUtf8.Length);
					}
				}

				return xml;
			}
		}

		private T DeserializeFromXml<T>(string xml) {
			using (var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(xml))) {
				using (var streamReader = new StreamReader(memoryStream, System.Text.Encoding.UTF8)) {
					var serializer = new XmlSerializer(typeof(T));
					return (T)serializer.Deserialize(streamReader);
				}
			}
		}


		public async Task SignDocumentAsync(string userIdentity, Guid personUniqueId, string documentUniqueId, string signedXml,
            ICryptoProviderService cryptoProvider) {
			if (documentUniqueId == null) {
				throw new ArgumentNullException(nameof(documentUniqueId));
			}

			if (signedXml == null) {
				throw new ArgumentNullException(nameof(signedXml));
			}

			if (cryptoProvider == null) {
				throw new ArgumentNullException(nameof(cryptoProvider));
			}
            var OSCPval = await cryptoProvider.VerifyExpiredCert(signedXml);

            if (OSCPval)
            {
                throw new SmartcontractException("Отозванный сертификат невозможно использовать в системе", ApiErrorCode.ValidationError);
            }

            using (var documentsRep = new Repository<Document>(_provider)) {
				var document = await documentsRep.Get(x => x.UniqueCode == documentUniqueId).SingleAsync();
				if (document.Status != DocumentStatusEnum.Drafts && document.Status != DocumentStatusEnum.OnSigning) {
					throw new SmartcontractException("Документ в текущем статусе не может быть подписан", ApiErrorCode.ValidationError);
				}

				var currentProfile = await GetCurrentPerson(userIdentity, personUniqueId, documentsRep);
				ValidateCMSSignature(signedXml, currentProfile);

				var isValid = await cryptoProvider.VerifyCMSAsync(document.RawXml, signedXml);
				if (!isValid) {
					throw new SmartcontractException("Подпись неверна", ApiErrorCode.ValidationError);
				}

				var previousStatus = document.Status;
				var nextStatus = GetNextStatusAfterSign(document);
				document.Status = nextStatus;

				if (previousStatus == DocumentStatusEnum.Drafts) {
					await _billingService.WriteOffBalanceAsync(userIdentity);
				}

				await documentsRep.UpdateAsync(document);
				await UpdateDocumentSummaryAsync(previousStatus, nextStatus, document, documentsRep);
				await _historyManager.WriteEntry(document, currentProfile,
					previousStatus == DocumentStatusEnum.Drafts ? "Документ подписан отправителем" : "Документ подписан получателем", documentsRep);                
                if (previousStatus == DocumentStatusEnum.Drafts)
                {
                   await DocumentSignedBySender(document,userIdentity);
                }
                else
                {
                   await DocumentSignedByRecipient(document,  userIdentity);
                }                
                await _historyManager.WriteSignHistoryEntryAsync(document, currentProfile, signedXml, documentsRep);
				await documentsRep.CommitAsync();
			}
		}

        public async Task DocumentSignedByRecipient(Document document, string userIdentity)
        {
            var profileRep = new Repository<PersonProfile>(_provider);
            var settingsNotificationRep = new Repository<NotificationSettings>(profileRep);
            var notificationSettinsRecipient= await settingsNotificationRep.Get(x => x.User.Email == userIdentity).SingleAsync();                
            var recipientContragent = document.RecipientContragent;                     
            var senderContragent = document.SenderPerson.Contragent;
            var senderUsers = profileRep.Get(x => x.UniqueId == document.SenderPerson.UniqueId)
                .SelectMany(x => x.Users).ToArray();                        
            var callbackUrlRecipient=new UriBuilder(_request.Scheme, _request.Host.Host, _request.Host.Port.Value,
                "/cabinet/viewer/inbox/signed").Uri.OriginalString;
            if (notificationSettinsRecipient.DocumentSign) {
                _emailService.SendEmail(userIdentity, "Вы подписали документ на Smartcontract.kz", $"Вы подписали электронные документы, " +
                   $"направленные Вам на подписание пользователем {senderContragent.FullName},{senderContragent.Xin}" +
                   $". Для просмотра электронных документов, пожалуйста, перейдите по следующей ссылке <a href='{callbackUrlRecipient}'>smartcontract.kz</a>"
                );
            }
            var callbackUrlSender=new UriBuilder(_request.Scheme, _request.Host.Host, _request.Host.Port.Value,
                "/cabinet/viewer/outgoing/signed").Uri.OriginalString;
            foreach (var sender in senderUsers) {
                var notificaitonSettingsRecipient = await settingsNotificationRep.Get(x => x.User == sender).SingleAsync();
                if (notificationSettinsRecipient.DocumentSign) {
                    _emailService.SendEmail(sender.Email, "Контрагент подписал документ на Smartcontract.kz",
                        $"Пользователь {recipientContragent.FullName}, {recipientContragent.Xin} подписал электронные документы, направленные ранее Вами на подписание." +                    
                        $"Для просмотра электронных документов, пожалуйста, перейдите по следующей ссылке  <a href='{callbackUrlSender}'>smartcontract.kz</a>"
                    );
                }                
            }
        }
        public async Task DocumentSignedBySender(Document document,string userIdentity)
        {
            var profileRep = new Repository<PersonProfile>(_provider);
            var settingsNotificationRep = new Repository<NotificationSettings>(profileRep);
            var notificationSettinsSender= await settingsNotificationRep.Get(x => x.User.Email == userIdentity).SingleAsync();            
            var recipientContragent = document.RecipientContragent;
            var recipientProfile = profileRep.Get(x => x.Contragent.Id == document.RecipientContragent.Id).Single();
            var recipientUsers = profileRep.Get(x => x.UniqueId == recipientProfile.UniqueId)
                .SelectMany(x => x.Users).ToArray();
            var senderContragent = document.SenderPerson.Contragent;
            string forewordSender = "на подписание электронные документы со следующим комментарием:";
            string forewordRecipient = "и оставил следующий комментарий: ";
            string summary = document.Summary;
            if (summary.IsEmpty())
            {
                forewordSender = "";
                forewordRecipient = "";
            }
            else
            {
                summary = summary.Insert(0, "«");
                summary = summary.Insert(summary.Length, "»");
            }                       
            var callbackUrlSender= new UriBuilder(_request.Scheme, _request.Host.Host, _request.Host.Port.Value,
                "/cabinet/viewer/outgoing/onsigning").Uri.OriginalString;            

            if (notificationSettinsSender.DocumentSign) {
                _emailService.SendEmail(userIdentity, "Вы подписали документ на Smartcontract.kz",
                    $"Вы подписали электронные документы, направленные пользователю {recipientContragent.FullName} , {recipientContragent.Xin}" +
                    $". Для просмотра электронных документов, пожалуйста, перейдите по следующей ссылке <a href='{callbackUrlSender}'>smartcontract.kz</a>");
            }

            if (notificationSettinsSender.DocumentSend) {
                _emailService.SendEmail(userIdentity, "Вы отправили документ на Smartcontract.kz", $"Вы направили пользователю " +
                  $"{recipientContragent.FullName}, {recipientContragent.Xin}" +
                  forewordSender +$"{summary}" +
                  $". Для просмотра электронных документов, пожалуйста, перейдите по следующей ссылке <a href='{callbackUrlSender}'>smartcontract.kz</a>"
                );
            }

            var callbackUrlRec = new UriBuilder(_request.Scheme, _request.Host.Host, _request.Host.Port.Value,
                "/cabinet/viewer/inbox/onsigning").Uri.OriginalString;           
                foreach (var recipient in recipientUsers) {
                    var notificationRecipient= await settingsNotificationRep.Get(x => x.User.Email == recipient.UserName).SingleAsync();
                    if (notificationRecipient.DocumentReceived) {
                        _emailService.SendEmail(recipient.Email, "Вы получили документ на Smartcontract.kz",
                            $"Пользователь {senderContragent.FullName}, {senderContragent.Xin} направил Вам на подписание электронные документы " +
                            forewordRecipient + $"{summary}." +
                            $"Для подписания электронных документов, пожалуйста, перейдите по следующей ссылке <a href='{callbackUrlRec}'>smartcontract.kz</a>"
                        );
                    }
                }                        
        }


        private async Task UpdateDocumentSummaryAsync(DocumentStatusEnum previousStatus, DocumentStatusEnum nextStatus, Document document,
			Repository baseRepository) {
			// Пока в таком виде, т.к. непонятно какие еще статусы будут и какие условия перехода между ними
			if (previousStatus == DocumentStatusEnum.Drafts && nextStatus == DocumentStatusEnum.OnSigning) {
				await DecrementDocumentsSummaryAsync(document.SenderPerson.Contragent, previousStatus, DocumentGroupTypeEnum.Outgoing,
					baseRepository);
				await IncrementDocumentsSummaryAsync(document.SenderPerson.Contragent, nextStatus, DocumentGroupTypeEnum.Outgoing, baseRepository);
				await IncrementDocumentsSummaryAsync(document.RecipientContragent, nextStatus, DocumentGroupTypeEnum.Inbox, baseRepository);
				return;
			}

			if (previousStatus == DocumentStatusEnum.OnSigning && nextStatus == DocumentStatusEnum.Signed) {
				await DecrementDocumentsSummaryAsync(document.RecipientContragent, previousStatus, DocumentGroupTypeEnum.Inbox, baseRepository);
				await IncrementDocumentsSummaryAsync(document.RecipientContragent, nextStatus, DocumentGroupTypeEnum.Inbox, baseRepository);

				await DecrementDocumentsSummaryAsync(document.SenderPerson.Contragent, previousStatus, DocumentGroupTypeEnum.Outgoing,
					baseRepository);
				await IncrementDocumentsSummaryAsync(document.SenderPerson.Contragent, nextStatus, DocumentGroupTypeEnum.Outgoing, baseRepository);
				return;
			}

			if (previousStatus == DocumentStatusEnum.OnSigning && nextStatus == DocumentStatusEnum.Rejected) {
				await DecrementDocumentsSummaryAsync(document.RecipientContragent, previousStatus, DocumentGroupTypeEnum.Inbox, baseRepository);
				await IncrementDocumentsSummaryAsync(document.RecipientContragent, nextStatus, DocumentGroupTypeEnum.Inbox, baseRepository);

				await DecrementDocumentsSummaryAsync(document.SenderPerson.Contragent, previousStatus, DocumentGroupTypeEnum.Outgoing,
					baseRepository);
				await IncrementDocumentsSummaryAsync(document.SenderPerson.Contragent, nextStatus, DocumentGroupTypeEnum.Outgoing, baseRepository);
				return;
			}

			if (previousStatus == DocumentStatusEnum.OnSigning && nextStatus == DocumentStatusEnum.Retired) {
				await DecrementDocumentsSummaryAsync(document.RecipientContragent, previousStatus, DocumentGroupTypeEnum.Inbox, baseRepository);
				await IncrementDocumentsSummaryAsync(document.RecipientContragent, nextStatus, DocumentGroupTypeEnum.Inbox, baseRepository);

				await DecrementDocumentsSummaryAsync(document.SenderPerson.Contragent, previousStatus, DocumentGroupTypeEnum.Outgoing,
					baseRepository);
				await IncrementDocumentsSummaryAsync(document.SenderPerson.Contragent, nextStatus, DocumentGroupTypeEnum.Outgoing, baseRepository);
				return;
			}
		}

		private DocumentStatusEnum GetNextStatusAfterSign(Document document) {
			if (document.Status == DocumentStatusEnum.Drafts) {
				return DocumentStatusEnum.OnSigning;
			}

			if (document.Status == DocumentStatusEnum.OnSigning) {
				return DocumentStatusEnum.Signed;
			}

			throw new InvalidOperationException("Невозможно выполнить переход из текущего статуса");
		}

		private async Task<PersonProfile> GetCurrentPerson(string userName, Guid personUniqueId, Repository baseRepository) {
			var userRep = new Repository<User>(baseRepository);
			return await userRep.Get(x => x.UserName == userName).SelectMany(x => x.PersonProfiles).Where(x => x.UniqueId == personUniqueId)
				.SingleAsync();
		}

		private IQueryable<T> GetCurrentPersonQuery<T>(string userName, Guid personUniqueId, Expression<Func<PersonProfile, T>> selectExpression,
			Repository baseRepository) {
			var userRep = new Repository<User>(baseRepository);
			return userRep.Get(x => x.UserName == userName).SelectMany(x => x.PersonProfiles).Where(x => x.UniqueId == personUniqueId)
				.Select(selectExpression);
		}

		private void ValidateCMSSignature(string cms, PersonProfile currentProfile) {
			var signedCms = CertificatesHelper.DecodeCmsFromString(cms);
			foreach (var certificate in signedCms.Certificates) {
				if (certificate.NotBefore > DateTime.Now) {
					throw new SmartcontractException("Срок действия ЭЦП еще не наступил", ApiErrorCode.ValidationError);
				}

				if (certificate.NotAfter < DateTime.Now) {
					throw new SmartcontractException("Срок действия ЭЦП истек", ApiErrorCode.ValidationError);
				}

				var information = CertificatesHelper.ReadX509CertificateCommonData(certificate);
				if (information.SerialNumber != currentProfile.Iin) {
					throw new SmartcontractException("Выбранная ЭЦП не соответствует ИИН текущего профиля", ApiErrorCode.ValidationError);
				}

				if (HasBin(currentProfile)) {
					if (!HasKeyUsage(certificate, Oid.FirstHead.Code)) {
						throw new SmartcontractException("Выбранная ЭЦП не принадлежит первому руководителю организации",
							ApiErrorCode.ValidationError);
					}

					if (information.Bin != currentProfile.Contragent.Xin) {
						throw new SmartcontractException("Выбранная ЭЦП не соответствует БИН текущей организации", ApiErrorCode.ValidationError);
					}
				}
				else {
					if (!HasKeyUsage(certificate, Oid.Physical.Code)) {
						throw new SmartcontractException("Выбранная ЭЦП не принадлежит ФЛ или ИП", ApiErrorCode.ValidationError);
					}
				}
			}
		}

		private bool HasBin(PersonProfile currentProfile) {
			// Пока что так, потом узнать как правильно валидировать ИП\ЮЛ\ФЛ
			return currentProfile.Iin != currentProfile.Contragent.Xin;
		}


		private bool HasKeyUsage(X509Certificate2 certificate, string oidCode) {
			var extension = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
			foreach (var keyUsage in extension.EnhancedKeyUsages) {
				if (keyUsage.Value == oidCode) {
					return true;
				}
			}

			return false;
		}


		public async Task RejectDocumentAsync(string userIdentity, Guid personUniqueId,DocumentRejectedRequest request) {
			if (personUniqueId == null) {
				throw new ArgumentNullException(nameof(personUniqueId));
			}

			if (request.UniqueId == null) {
				throw new ArgumentNullException(nameof(request.UniqueId));
			}

			using (var documentsRep = new Repository<Document>(_provider)) {
				var document = await documentsRep.Get(x => x.UniqueCode ==request.UniqueId).SingleAsync();
				if (document.Status != DocumentStatusEnum.OnSigning) {
					throw new SmartcontractException("Документ в текущем статусе не может быть отклонен", ApiErrorCode.ValidationError);
				}
				var personProfile = await GetCurrentPerson(userIdentity, personUniqueId, documentsRep);
				if (document.RecipientContragent.Id != personProfile.Contragent.Id) {
					throw new SmartcontractException("Пользователь не имеет прав доступа к документу", ApiErrorCode.Forbidden);
				}

				var previousStatus = document.Status;
				var nextStatus = DocumentStatusEnum.Rejected;
				document.Status = nextStatus;
                document.ReasonForRejection = request.Cause;
                await DocumentRejectedMailingByAll(document, userIdentity);
				await documentsRep.UpdateAsync(document);
				await UpdateDocumentSummaryAsync(previousStatus, nextStatus, document, documentsRep);
				await _historyManager.WriteEntry(document, personProfile, "Документ отклонен получателем", documentsRep);
				await documentsRep.CommitAsync();                
			}
		}
        public async Task DocumentRejectedMailingByAll(Document document, string userIdentity)
        {          
            var profileRep = new Repository<PersonProfile>(_provider);
            var settingsNotificationRep = new Repository<NotificationSettings>(profileRep);
            var settingsNotificationRecipient = await settingsNotificationRep.Get(x => x.User.Email == userIdentity).SingleAsync();
            var recipientContragent = document.RecipientContragent;
            var senderContragent = document.SenderPerson.Contragent;
            var senderUsers = profileRep.Get(x => x.UniqueId == document.SenderPerson.UniqueId)
                .SelectMany(x => x.Users).ToArray();                      
            var callbackUrlRecipient = new UriBuilder(_request.Scheme, _request.Host.Host, _request.Host.Port.Value,
                "/cabinet/viewer/inbox/rejected").Uri.OriginalString;          
            string foreword = " со следующим комментарием:";           
            string summary = document.ReasonForRejection;
            if (!summary.IsEmpty())
            {
                summary= summary.Insert(0, foreword);
            }
            else
            {
                summary = "";
            }

            if (settingsNotificationRecipient.DocumentRejected) {
                _emailService.SendEmail(userIdentity, "Вы отклонили документ на Smartcontract.kz", $"Вы отклонили направленные" +
                  $" Вам на подписание пользователем {senderContragent.FullName},{senderContragent.Xin} {summary} . " +
                  $"Для просмотра электронных документов, пожалуйста, перейдите по следующей ссылке <a href='{callbackUrlRecipient}'>smartcontract.kz</a>"
                );
            }                        
            var callbackUrlSender = new UriBuilder(_request.Scheme, _request.Host.Host, _request.Host.Port.Value,
                "/cabinet/viewer/outgoing/rejected").Uri.OriginalString;
            foreach (var sender in senderUsers) {
                var settingsNotificationSender =await  settingsNotificationRep.Get(x => x.User == sender).SingleAsync();
                if (settingsNotificationSender.DocumentRejected) {
                    _emailService.SendEmail(sender.Email, "Контрагент отклонил документ на Smartcontract.kz",
                        $"Пользователь {recipientContragent.FullName}, {recipientContragent.Xin} отклонил направленные ранее Вами на подписание электронные документы  {summary} ." +
                        $"Для просмотра электронных документов, пожалуйста, перейдите по следующей ссылке  <a href='{callbackUrlSender}'>smartcontract.kz</a>"
                    );
                }
            }
        }       
        public async Task RetireDocumentAsync(string userIdentity, Guid personUniqueId, string documentUniqueId) {
			if (personUniqueId == null) {
				throw new ArgumentNullException(nameof(personUniqueId));
			}

			if (documentUniqueId == null) {
				throw new ArgumentNullException(nameof(documentUniqueId));
			}

			using (var documentsRep = new Repository<Document>(_provider)) {
				var document = await documentsRep.Get(x => x.UniqueCode == documentUniqueId).SingleAsync();
				if (document.Status != DocumentStatusEnum.OnSigning) {
					throw new SmartcontractException("Документ в текущем статусе не может быть отозван", ApiErrorCode.ValidationError);
				}
				var personProfile = await GetCurrentPerson(userIdentity, personUniqueId, documentsRep);
				if (document.SenderPerson.Contragent.Id != personProfile.Contragent.Id) {
					throw new SmartcontractException("Пользователь не имеет прав доступа к документу", ApiErrorCode.Forbidden);
				}

				var previousStatus = document.Status;
				var nextStatus = DocumentStatusEnum.Retired;
				document.Status = nextStatus;
				await documentsRep.UpdateAsync(document);
				await UpdateDocumentSummaryAsync(previousStatus, nextStatus, document, documentsRep);
				await _historyManager.WriteEntry(document, personProfile, "Документ отозван отправителем", documentsRep);
				await documentsRep.CommitAsync();
                await DocumentRetiredByAll(document, userIdentity);
            }
		}      
        public async Task DocumentRetiredByAll(Document document, string userIdentity)
        {
            var profileRep = new Repository<PersonProfile>(_provider);
            var settingsNotificationRep = new Repository<NotificationSettings>(profileRep);
            var recipientContragent = document.RecipientContragent;
            var recipientProfile = profileRep.Get(x => x.Contragent.Id == document.RecipientContragent.Id).Single();
            var recipientUsers = profileRep.Get(x => x.UniqueId == recipientProfile.UniqueId)
                .SelectMany(x => x.Users).ToArray();
            var senderContragent = document.SenderPerson.Contragent;           
            var callbackUrlSender = new UriBuilder(_request.Scheme, _request.Host.Host, _request.Host.Port.Value,
                "/cabinet/viewer/outgoing/retired").Uri.OriginalString;
            var settingsNotificationRecipient = await settingsNotificationRep.Get(x => x.User.Email == userIdentity).SingleAsync();
            if (settingsNotificationRecipient.DocumentRetired) {
                _emailService.SendEmail(userIdentity, "Вы отозвали документ на Smartcontract.kz", $"Вы отозвали электронные документы, " +
                $"направленные Вами на подписание пользователю " +
                $"{recipientContragent.FullName}, {recipientContragent.Xin}" +
                $". Для просмотра электронных документов, пожалуйста, перейдите по следующей ссылке <a href='{callbackUrlSender}'>smartcontract.kz</a>"
                );
            }
            var callbackUrlRec = new UriBuilder(_request.Scheme, _request.Host.Host, _request.Host.Port.Value,
                "/cabinet/viewer/inbox/retired").Uri.OriginalString;
            foreach (var recipient in recipientUsers) {
                var notificationSettingsSender =await settingsNotificationRep.Get(x => x.User == recipient).SingleAsync();
                if (notificationSettingsSender.DocumentRetired) {
                    _emailService.SendEmail(recipient.Email, "Контрагент отозвал документ на Smartcontract.kz",
                        $"Пользователь {senderContragent.FullName}, {senderContragent.Xin} ] отозвал электронные документы," +
                        $" направленные ранее Вам на подписание." +
                        $"Для просмотра электронных документов, пожалуйста, перейдите по следующей ссылке " +
                        $"<a href='{callbackUrlRec}'>smartcontract.kz</a>"
                    );
                }
            }
        }

        public async Task<DocumentAttachmentViewModel[]> GetAttachmentsAsync(string identityName, Guid personUniqueId, string documentUniqueId) {
			using (var repository = new Repository<DocumentAttachment>(_provider)) {
				return (await repository.Get(x => x.Document.UniqueCode == documentUniqueId).Select(x => new DocumentAttachmentViewModel() {
					FileName = x.FileName,
					Size = x.Size,
					Uploaded = x.Uploaded,
					UniqueId = x.Id,
				}).ToListAsync())
					.ToArray();
			}
		}

		public async Task<DocumentCardModel> GetDocumentCardAsync(string identityName, Guid personUniqueId, string documentUniqueId) {
			using (var repository = new Repository<Document>(_provider)) {
				var card = await repository.Get(x => x.UniqueCode == documentUniqueId).Select(x => new {
					BasementDocumentId = x.ParentDocument.UniqueCode,
					InternalNumber = x.Number,
					SenderFullName = x.SenderPerson.Contragent.FullName,
					RecipientFullName = x.RecipientContragent.FullName,
					RecipientUniqueId = x.RecipientContragent.UniqueId,
					SenderUniqueId = x.SenderPerson.Contragent.UniqueId,
					RegistrationDate = x.RegistrationDate,
					SystemRegistrationDate = x.SystemRegistrationDate,
					Summary = x.Summary,
					x.Status
				}).SingleOrDefaultAsync();
				if (card == null) {
					throw new SmartcontractException($"Документ с номером {documentUniqueId} не найден", ApiErrorCode.ResourceNotFound);
				}

				var currentContragentUniqueId = await GetCurrentPersonQuery(identityName, personUniqueId, x => x.Contragent.UniqueId, repository)
					.SingleOrDefaultAsync();
				return new DocumentCardModel {
					CanBeSigned = CanBeSigned(card.Status, card.SenderUniqueId, card.RecipientUniqueId, currentContragentUniqueId),
					CanBeEdited = CanBeEdited(card.Status, card.SenderUniqueId, currentContragentUniqueId),
					CanBeRejected = CanBeRejected(card.Status, card.RecipientUniqueId, currentContragentUniqueId),
					CanBeRetired = CanBeRetired(card.Status, card.SenderUniqueId, currentContragentUniqueId),
					BasementDocumentId = card.BasementDocumentId,
					InternalNumber = card.InternalNumber,
					SenderFullName = card.SenderFullName,
					RecipientUniqueId = card.RecipientUniqueId,
					RegistrationDate = card.RegistrationDate,
					RecipientFullName = card.RecipientFullName,
					Summary = card.Summary,
					SystemRegistrationDate = card.SystemRegistrationDate
				};
			}
		}

		private bool CanBeRejected(DocumentStatusEnum documentStatus, string recipientProfileUniqueId, string currentProfileUniqueId) {
			if (documentStatus == DocumentStatusEnum.OnSigning) {
				return recipientProfileUniqueId == currentProfileUniqueId;
			}

			return false;
		}

		private bool CanBeRetired(DocumentStatusEnum documentStatus, string senderProfileUniqueId, string currentProfileUniqueId) {
			if (documentStatus == DocumentStatusEnum.OnSigning) {
				return senderProfileUniqueId == currentProfileUniqueId;
			}

			return false;
		}

		private bool CanBeSigned(DocumentStatusEnum documentStatus, string senderContragentUniqueId, string recipientProfileUniqueId,
			string currentContragentUniqueId) {
			if (documentStatus == DocumentStatusEnum.Drafts) {
				return senderContragentUniqueId == currentContragentUniqueId;
			}

			if (documentStatus == DocumentStatusEnum.OnSigning) {
				return recipientProfileUniqueId == currentContragentUniqueId;
			}

			return false;
		}

		private bool CanBeEdited(DocumentStatusEnum documentStatus, string senderProfileUniqueId, string currentProfileUniqueId) {
			if (documentStatus == DocumentStatusEnum.Drafts) {
				return senderProfileUniqueId == currentProfileUniqueId;
			}

			return false;
		}

		public async Task<byte[]> CreatePackageAsync(string userName, Guid personUniqueId, string documentUniqueId) {
			using (var repository = new Repository<Document>(_provider)) {
				var person = await GetCurrentPersonQuery(userName, personUniqueId, x => new { PersonId = x.Id, ContragentId = x.Contragent.Id },
					repository).SingleAsync();
				var document = await repository.Get(x =>
						x.UniqueCode == documentUniqueId && (x.SenderPerson.Id == person.PersonId || x.RecipientContragent.Id == person.ContragentId))
					.FirstOrDefaultAsync();

				if (document == null) {
					throw new SmartcontractException("Документ не найден", ApiErrorCode.ValidationError);
				}

				if (document.Status == DocumentStatusEnum.Drafts) {
					throw new SmartcontractException("Невозможно сформировать пакет для не подписанного документа", ApiErrorCode.ValidationError);
				}

				var attachmentRep = new Repository<DocumentAttachment>(repository);
				var signatureRep = new Repository<SignHistoryItem>(repository);
				var signatures = await signatureRep.Get(x => x.Document.Id == document.Id).Select(x => x.Signature).ToAsyncEnumerable().ToArray();
				var documentAttachments = attachmentRep.Get(x => x.Document.Id == document.Id).Select(x => new {
					x.StorageId,
					x.FileName
				}).ToArray();
				var encoding = new UTF8Encoding(false);
				var outputStream = new MemoryStream();
				using (var zip = new ZipFile(encoding)) {
					zip.AddEntry("source.sm", document.RawXml, encoding);

					zip.AddDirectoryByName("signatures");
					for (var i = 0; i < signatures.Length; i++) {
						zip.AddEntry(Path.Combine("signatures", $"signature{i + 1}.sm"), signatures[i], encoding);
					}

					zip.AddDirectoryByName("attachments");
					var bucket = _fileStorageProvider.GetDocumentBucket();
					foreach (var documentAttachment in documentAttachments) {
						var bytes = await bucket.DownloadAsBytesAsync(new ObjectId(documentAttachment.StorageId));
						var fileStream = new MemoryStream(bytes);
						zip.AddEntry(Path.Combine("attachments", documentAttachment.FileName), fileStream);
					}

					zip.Save(outputStream);
				}

				var archiveContent = outputStream.ToArray();
				return archiveContent;
			}
		}

        public async Task<CustomFile> GetFileAsync(string userName, Guid personUniqueId, string documentUniqueId,long fileId)
        {
            using (var repository = new Repository<Document>(_provider))
            {
                var person = await GetCurrentPersonQuery(userName, personUniqueId, x => new { PersonId = x.Id, ContragentId = x.Contragent.Id },
                    repository).SingleAsync();
                var document = await repository.Get(x =>
                        x.UniqueCode == documentUniqueId && (x.SenderPerson.Id == person.PersonId || x.RecipientContragent.Id == person.ContragentId))
                    .FirstOrDefaultAsync();

                if (document == null)
                {
                    throw new SmartcontractException("Документ не найден", ApiErrorCode.ValidationError);
                }               

                var attachmentRep = new Repository<DocumentAttachment>(repository);
                var signatureRep = new Repository<SignHistoryItem>(repository);
                var signatures = await signatureRep.Get(x => x.Document.Id == document.Id).Select(x => x.Signature).ToAsyncEnumerable().ToArray();
                var documentAttachments = attachmentRep.Get(x => x.Document.Id == document.Id).Select(x => new {
                    x.Id,
                    x.StorageId,
                    x.FileName,
                    x.ContentType
                }).ToArray();             
                              
                var bucket = _fileStorageProvider.GetDocumentBucket();
                var file = documentAttachments.Where(x => x.Id == fileId).Single();
                var ms = new MemoryStream( await bucket.DownloadAsBytesAsync (new ObjectId( file.StorageId)));
                var customFile = new CustomFile
                {
                    FileContents = await bucket.DownloadAsBytesAsync(new ObjectId(file.StorageId)),
                    ContentType = file.ContentType,
                    FileName = file.FileName
                };
                return customFile;
                    
            }
        }

		public async Task<DocumentValidationModel> UploadAndValidatePackageAsync(ICryptoProviderService cryptoProvider, IFormFile attachmentFile) {
            if(!attachmentFile.ContentType.Contains("zip"))
                return new DocumentValidationModel() { ValidationResult = $"Файл не соответствует *.zip формату" };
            var ms = new MemoryStream();
			await attachmentFile.CopyToAsync(ms);
			ms.Position = 0;
			var attachments = new List<DocumentAttachmentValidationModel>();
			var source = string.Empty;
			var base64Signatures = new Dictionary<string, string>();
			var encoding = new UTF8Encoding(false);
			using (var zip = ZipFile.Read(ms, new ReadOptions() { Encoding = encoding })) {
				var sourceEntry = zip.Entries.SingleOrDefault(x => x.FileName == "source.sm");
				if (sourceEntry == null) {
					return new DocumentValidationModel() { ValidationResult = "Файл подписи не найден" };
				}

				source = GetStringContent(sourceEntry);
				var signaturesEntries = zip.SelectEntries("*.sm", "signatures");
				var attachmentsEntries = zip.SelectEntries("*.*", "attachments");
				foreach (var entry in attachmentsEntries) {
					attachments.Add(new DocumentAttachmentValidationModel {
						FileName = Path.GetFileName(entry.FileName),
						Size = (int)entry.UncompressedSize,
						ContentHash = Helper.CalculateSha1Hash(GetBytes(entry))
					});
				}

				foreach (var entry in signaturesEntries) {
					base64Signatures.Add(Path.GetFileName(entry.FileName), GetStringContent(entry));
				}
			}

			foreach (var signature in base64Signatures) {
				var isValid = await cryptoProvider.VerifyCMSAsync(source, signature.Value);
				if (!isValid) {
					return new DocumentValidationModel() { ValidationResult = $"Подпись {signature.Key} не соответствует исходным данным" };
				}
			}

			return await ValidateDocumentAsync(source, attachments.ToArray());
		}

		private string GetStringContent(ZipEntry sourceEntry) {
			return Encoding.UTF8.GetString(GetBytes(sourceEntry));
		}

		private byte[] GetBytes(ZipEntry sourceEntry) {
			var ms = new MemoryStream();
			sourceEntry.Extract(ms);
			return ms.ToArray();
		}

		private async Task<DocumentValidationModel> ValidateDocumentAsync(string signature, DocumentAttachmentValidationModel[] attachments) {
			var documentContainer = DeserializeFromXml<DocumentContainer>(signature);
			var result = new DocumentValidationModel {
				UniqueCode = documentContainer.UniqueCode,
				DocumentType = (DocumentTypeEnum)documentContainer.DocumentType,
				Number = documentContainer.InternalNumber,
				SenderPersonIin = documentContainer.SenderPersonIin,
				SenderContragentXin = documentContainer.SenderContragentXin,
				RecipientContragentXin = documentContainer.RecipientContragentXin,
				RegistrationDate = documentContainer.RegistrationDate,
				Summary = documentContainer.Description,
			};

			//TODO: Валидация подписей

			using (var repository = new Repository<Document>(_provider)) {
				var document = await repository.Get(x => x.UniqueCode == result.UniqueCode).SingleOrDefaultAsync();
				if (document == null) {
					result.ValidationResult = $"Документ с уникальным номером: {result.UniqueCode} в системе не зарегистрирован";
					return result;
				}

				if (document.DocumentType != (DocumentTypeEnum)documentContainer.DocumentType) {
					result.ValidationResult = "Типы документов не совпадают";
					return result;
				}

				if (document.RegistrationDate != documentContainer.RegistrationDate) {
					result.ValidationResult = "Дата регистрации документов не совпадают";
					return result;
				}

				if (attachments.Any()) {
					var documentAttachmentRep = new Repository<DocumentAttachment>(repository);
					var documentAttachments = documentAttachmentRep.Get(x => x.Document.Id == document.Id).ToArray();
					if (documentAttachments.Length != attachments.Length) {
						result.ValidationResult = "Количество прикрепленных файлов к документу не совпадают";
						return result;
					}

					foreach (var documentAttachment in documentAttachments) {
						var attachment = attachments.SingleOrDefault(x => x.FileName == documentAttachment.FileName);
						if (attachment == null || attachment.ContentHash != documentAttachment.ContentHash) {
							result.ValidationResult = "Прикрепленные к документу файлы не совпадают";
							return result;
						}

						attachment.ContentType = documentAttachment.ContentType;
					}

					result.Attachments = attachments;
				}
			}

			return result;
		}
	}
}