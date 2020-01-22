using System;
using System.Threading.Tasks;
using Billing.DataContracts.Requests;
using Billing.DataContracts.Responses;
using Common.DAL.Abstraction.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NHibernate.Linq;
using Smartcontract.App.Infrastructure.DevExtreme;
using Smartcontract.App.Infrastructure.Services.Implementation;
using Common.DataContracts.Base;
using Smartcontract.App.Managers.QazKom;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities;

namespace Smartcontract.App.Controllers {
	[ApiController]
	[Route("api/[controller]")]
	[Authorize]
	public class PacketsController : Controller {
		[HttpGet]
		[ProducesResponseType(200, Type = typeof(ApiResponse<ActivateByCodeResponse>))]
		public async Task<IActionResult> Get([FromServices] RemoteBillingService billing, DataSourceLoadOptionsImpl options) {
			return Ok(await billing.GetPacketsAsync(User.Identity.Name, options));
		}

		[HttpGet("[action]")]
		[ProducesResponseType(200, Type = typeof(ApiResponse<ActivateByCodeResponse>))]
		public async Task<IActionResult> History([FromServices] RemoteBillingService billing, DataSourceLoadOptionsImpl options) {
			return Ok(await billing.GetPacketsHistoryAsync(User.Identity.Name, options));
		}

		[HttpPost("[action]")]
		[ProducesResponseType(200, Type = typeof(ApiResponse<ActivateByCodeResponse>))]
		public async Task<IActionResult> Activate([FromServices] RemoteBillingService billing, [FromServices] Provider provider, [FromBody] ActivateByCodeRequest request) {
			using (var rep = new Repository<ActivationCode>(provider)) {
				var code = await rep.Get(x => x.Number == request.Number && x.Code == request.Code).SingleOrDefaultAsync();
				if (code == null) {
					return Ok(ApiResponse.Failed(ApiErrorCode.ResourceNotFound, "Активационная карта с указанными данными не найдена"));
				}
				if (code.Activated.HasValue) {
					return Ok(ApiResponse.Failed(ApiErrorCode.ResourceNotFound, "Активационная карта с указанными данными уже активирована"));
				}

				var packetId = await billing.AddPacketAsync(code.PacketType.Code, User.Identity.Name);
				code.Activated = DateTime.Now;
				code.PacketId = packetId;

				await rep.UpdateAsync(code);
				await rep.CommitAsync();
				return Ok(ApiResponse.Success(new ActivateByCodeResponse() { PacketName = code.PacketType.Name }));
			}
		}

		[HttpPost("[action]")]
		[ProducesResponseType(200, Type = typeof(ApiResponse<ActivateByCodeResponse>))]
		public async Task<IActionResult> Buy([FromServices] QazKomAckquiringService qazKomAckquiring, [FromServices] Provider provider, [FromBody] BuyPacketRequest request) {
			int price = 0;
			using (var rep = new Repository<PacketType>(provider)) {
				var type = await rep.Get(x => x.Code == request.PacketType).SingleOrDefaultAsync();
				if (type == null) {
					return Ok(ApiResponse.Failed(ApiErrorCode.ResourceNotFound, "Выбраный тип пакета не найден"));
				}
				if (type.Price == 0) {
					return Ok(ApiResponse.Failed(ApiErrorCode.ValidationError, "Выбраный тип пакета не предназначен для покупки"));
				}
				price = type.Price;
			}
			var builder = new UriBuilder(this.Request.Scheme, this.Request.Host.Host);
			if (this.Request.Host.Port.HasValue) {
				builder.Port = this.Request.Host.Port.Value;
			}
			var response = await qazKomAckquiring.CreateTransaction(User.Identity.Name, price, $"Покупка пакета {request.PacketType}", request.PacketType, builder.Uri);
			return Ok(ApiResponse.Success(response));
		}
	}

	public class BuyPacketRequest {
		public string PacketType { get; set; }
	}
}