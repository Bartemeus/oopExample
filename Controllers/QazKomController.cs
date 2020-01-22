using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using Smartcontract.App.Infrastructure.Services.Implementation;
using Smartcontract.App.Managers.QazKom;

namespace Smartcontract.App.Controllers {
	[Route("api/[controller]")]
	public class QazKomController : ControllerBase {
		[HttpPost("[action]")]
		public async Task<IActionResult> Complete(
			[FromServices] QazKomAckquiringService qazKom,
			[FromServices]RemoteBillingService billing,
			[FromQuery]string orderId,
			string response) {
			Log.Information("{EventId} query with {orderId}, {response}", "QAZKOM", orderId, response);
			var transaction = await qazKom.TryUpdateTransactionByOrderIdAsync(orderId);
			await billing.AddPacketAsync(transaction.ProductCode, transaction.Employee);
			await qazKom.CompleteTransaction(transaction);
			Log.Information("{EventId} transaction completed {orderId}", "QAZKOM", orderId);
			return Ok(0);
		}

		[HttpPost("[action]")]
		public async Task<IActionResult> Failed([FromServices] QazKomAckquiringService qazKom, [FromQuery]string orderId, string response) {
			Log.Information("{EventId} failed {orderId}, {response}", "QAZKOM", orderId, response);
			await qazKom.TryUpdateTransactionByOrderIdAsync(orderId);
			return Ok(0);
		}
	}
}