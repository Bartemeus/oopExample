using System;
using System.Linq;
using System.Threading.Tasks;
using Common.DAL.Abstraction.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smartcontract.Constants;
using Smartcontract.DataContracts.Navigator;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities;
using Smartcontract.DAL.Entities.References;
using Common.DataContracts.Base;

namespace Smartcontract.App.Controllers {
	[Route("api/[controller]")]
	[ApiController]
	[Authorize]
	public class NavigatorController : Controller {
		private readonly Provider _provider;

		public NavigatorController(Provider provider) {
			_provider = provider;
		}
        [HttpGet]
        [ProducesResponseType(200, Type = typeof(ApiResponse<NavigatorResponse[]>))]
        public IActionResult Get([FromHeader]Guid personUniqueId, DocumentGroupTypeEnum documentGroupType)
        {
            using (var userRepository = new Repository<User>(_provider))
            {
                var contragentId = userRepository.Get(x => x.UserName == User.Identity.Name).SelectMany(x => x.PersonProfiles)
                    .Where(x => x.UniqueId == personUniqueId).Select(x => (long?)x.Contragent.Id).FirstOrDefault();
                if (!contragentId.HasValue)
                {
                    return Json(ApiResponse.Failed(ApiErrorCode.ValidationError, $"Данные недоступны для текущего профиля"));
                }

                var documentRepository = new Repository<DocumentsSummary>(userRepository);
                var documentSummary = documentRepository.Get(x => x.Contragent.Id == contragentId && x.DocumentGroupType == documentGroupType).GroupBy(x => new { x.Status }).Select(x => new
                {
                    DocumentStatus = x.Key.Status,
                    NewDocumentsCount = x.Sum(s => s.NewDocumentsCount),
                    DocumentsCount = x.Sum(s => s.DocumentsCount)
                }).ToArray();

                var documentStatusRepository = new Repository<RefDocumentStatus>(documentRepository);
                var response = documentStatusRepository.Get(x => x.DocumentGroupType == documentGroupType).Select(x => new
                {
                    Status = x.DocumentStatus
                }).ToArray().Select(x => new NavigatorResponse
                {
                    Status = x.Status.ToString().ToLower()
                }).ToArray();

                foreach (var data in documentSummary)
                {
                    var responseItem = response.First(x => x.Status == data.DocumentStatus.ToString().ToLower());
                    responseItem.Status = responseItem.Status.ToLower();
                    responseItem.NewDocumentsCount = data.NewDocumentsCount;
                    responseItem.DocumentsCount = data.DocumentsCount;
                }

                return Json(ApiResponse.Success(response));
            }
        }
    }
}