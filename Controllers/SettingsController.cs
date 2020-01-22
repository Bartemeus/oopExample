using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common.DataContracts.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smartcontract.App.Infrastructure.Services.Abstraction;
using Smartcontract.App.Infrastructure.Services.Implementation;
using Smartcontract.DAL;
using Smartcontract.DataContracts.Settings;

namespace Smartcontract.App.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    public class SettingsController : Controller {
        private readonly Provider _provider;
        private readonly ISettingsManager settingsManager;
        public SettingsController(Provider provider,ISettingsManager manager) {
            _provider = provider;
            settingsManager = manager;
        }
        [HttpPost]
        [ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
        public async Task<IActionResult> Post([FromBody] NotificationSettingsRequest request) {
            await settingsManager.SaveNotificationSettings(User.Identity.Name, request);
            return Json(ApiResponse.Success(true));
        }

        [HttpGet]
        [ProducesResponseType(200, Type = typeof(ApiResponse<NotificationSettingsResponse>))]
        public async Task<IActionResult> Get() {
            var email = User.Identity.Name;
            var settings= await settingsManager.GetNotificationSettings(email);
            return Json(ApiResponse.Success(settings));
        }
    }
}