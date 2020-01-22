using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Smartcontract.App.Reports.Models;
using Smartcontract.App.Reports.Services.Abstractions;
using Smartcontract.App.Reports.Templates;

namespace Smartcontract.App.Controllers
{
    [Route("api/[controller]")]
	//[Authorize]
	public class ReportingController : Controller {

		public ReportingController() {
		}
        
        /// <summary>
        /// Возвращает blob карточки документа
        /// </summary>
        /// <param name="service">Сервис отчетов DevExpress</param>
        /// <param name="documentUniqueCode">Уникальный идентификатор документа</param>
        /// <returns></returns>
		[HttpGet("[action]")]
		public async Task<IActionResult> DocumentCard([FromServices] IReportService service, [FromQuery]string documentUniqueCode) {
			var report = new DocumentCardReport();
			var model = await service.GetModelAsync(documentUniqueCode);
            //dataSource для отчета может быть только коллекция
			var dataSource = new List<OtherDocumentCardModel> {
				model
			};
            var link = Url.Action(
                    "DocumentCard",
                    "Reporting",
                    new { documentUniqueCode = documentUniqueCode },
                    protocol: HttpContext.Request.Scheme);

            report.DataSource = dataSource;
            report.Parameters["QrCodeValue"].Value = link;

            byte[] result;
			using (var stream = new MemoryStream()) {
				report.ExportToPdf(stream);
				result = stream.ToArray();
			}

			return File(result, "application/pdf");
		}
	}
}