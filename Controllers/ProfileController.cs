using System;
using System.Linq;
using System.Threading.Tasks;
using DevExtreme.AspNet.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Smartcontract.App.Infrastructure.DevExtreme;
using Smartcontract.App.Managers;
using Common.DataContracts.Base;
using Common.DAL.Abstraction.Repositories;
using Smartcontract.DataContracts.Profile;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities;
using Smartcontract.App.Infrastructure.Services.Abstraction;
using System.ComponentModel.DataAnnotations;
using PhoneAttribute = Smartcontract.DataContracts.Attribute.PhoneAttribute;
using System.Collections.Generic;
using System.IO;
using Smartcontract.App.Managers.Models;

namespace Smartcontract.App.Controllers {
	[Route("api/[controller]")]
	[ApiController]
	[Authorize]
	public class ProfileController : Controller {
        private readonly Provider _provider;
        public ProfileController(Provider provider) {
            _provider = provider;
        }
		[HttpGet]
		[ProducesResponseType(200, Type = typeof(ApiResponse<ProfileResponse[]>))]
		public async Task<IActionResult> Get([FromServices] ProfileManager profileManager) {
			var response = await profileManager.GetProfiles(User.Identity.Name);
			return Json(ApiResponse.Success(response));
		}

		[HttpDelete]
		[ProducesResponseType(200, Type = typeof(ApiResponse<Guid>))]
		public async Task<IActionResult> Delete([FromServices] ProfileManager profileManager, 
            [FromQuery] string uniqueId, [FromServices] IEmailService emailService) {
			var defaultProfileUniqueId= await profileManager.RemoveProfile(new Guid(uniqueId), User.Identity.Name,emailService);
			return Json(ApiResponse.Success(defaultProfileUniqueId));
		}

        [HttpPost("[action]")]
        [ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
        public async Task<IActionResult> Change([FromForm] ProfileChangeRequest request,
        [FromServices] ProfileManager profileManager) {                                                                     
            await profileManager.ChangeProfile(request);
            return Json(ApiResponse.Success(true));
        }   
        //пока так 
        [AllowAnonymous]
        [HttpGet("[action]")]
        [ResponseCache(Location =ResponseCacheLocation.Any, Duration =10)]
        public async Task<IActionResult> GetAvatar([FromQuery] string uniqueId,[FromServices] ProfileManager profileManager) {
            var avatar = await profileManager.GetAvatar(new Guid(uniqueId));
            var ms = new MemoryStream(avatar.Avatar);
            return File(ms,avatar.ContentType,avatar.FileName);
        } 
        
        [HttpPost("[action]")]
        [ProducesResponseType(200, Type = typeof(ApiResponse<bool>))]
        public async Task<IActionResult> AddProfile([FromBody] ProfileCreateRequest request,[FromServices] ProfileManager profileManager,
            [FromServices] IEmailService emailService) {            
            await profileManager.CreateOrAttachToProfile(request, false, User.Identity.Name, new Repository<User>(_provider),emailService);
            return Json(ApiResponse.Success(true));
        }

		[HttpGet("[action]")]
		public async Task<IActionResult> Recipients(
			[FromServices] ProfileManager profileManager,
			[FromServices] Provider provider,
			[FromHeader]Guid personUniqueId,
			[FromQuery] DataSourceLoadOptionsImpl options) {
			using (var rep = new Repository<Contragent>(provider)) {
				var query = await profileManager.GetRecipientsAsync(personUniqueId, User.Identity.Name, rep);
				return this.JsonEx(DataSourceLoader.Load(query, options));
			}
		}
	}    
}