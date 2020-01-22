using System.Linq;
using System.Threading.Tasks;
using Common.DAL.Abstraction.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NHibernate.Linq;
using Smartcontract.DataContracts.References;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities.References;
using Common.DataContracts.Base;

namespace Smartcontract.App.Controllers {
	[Route("api/[controller]")]
	[ApiController]
	[Authorize]
	public class ReferenceController : Controller {
		private readonly Provider _provider;

		public ReferenceController(Provider provider) {
			_provider = provider;
		}

		[HttpGet("[action]")]
		[AllowAnonymous]
		[ProducesResponseType(200, Type = typeof(ApiResponse<DocumentStatusReferenceResponse[]>))]
		public async Task<IActionResult> DocumentStatuses() {
			using (var repository = new Repository<RefDocumentStatus>(_provider)) {
				var response = await repository.Get().Select(x => new DocumentStatusReferenceResponse {
					DocumentStatus = x.DocumentStatus,
					DocumentGroupType = x.DocumentGroupType,
					Name = x.Name,
					NameEn = x.NameEn,
					NameKz = x.NameKz
				}).ToListAsync();
				return Json(ApiResponse.Success(response.ToArray()));
			}
		}
	}
}