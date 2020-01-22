using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common.DataContracts.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smartcontract.App.Infrastructure.Services.Abstraction;
using Smartcontract.App.Infrastructure.Services.Implementation.Email;
using Smartcontract.App.Infrastructure.Services.Implementation.Email.Config;
using Smartcontract.DataContracts.Consultation;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Smartcontract.App.Controllers
{
    [Route("api/[controller]")]
    [AllowAnonymous]
    public class ConsultationController : Controller
    {
        private readonly EmailSenderConfig _emailSenderConfig;
        public ConsultationController(EmailSenderConfig emailSenderConfig)
        {
            _emailSenderConfig = emailSenderConfig;
        }
        [HttpPost("[action]")]
        public IActionResult GetConsultPartner([FromBody] ConsultationRequest request,[FromServices] IEmailService emailService)
        {
            emailService.SendEmail(_emailSenderConfig.InfoEmail,"Консультация-хочу стать партнером", 
                $"Имя:{request.Name}Телефон:{request.Phone}");
            return Json(ApiResponse.Success(true));
        }
        [HttpPost("[action]")]
        public IActionResult GetConsult([FromBody] ConsultationRequest request, [FromServices] IEmailService emailService)
        {
            emailService.SendEmail(_emailSenderConfig.InfoEmail, "Консультация-получить консультацию",
                $"Имя:{request.Name}Телефон:{request.Phone}");            
            return Json(ApiResponse.Success(true));
        }
    }    
}
