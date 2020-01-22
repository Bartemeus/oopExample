using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Common.DAL.Abstraction.Repositories;
using NHibernate.Linq;
using Smartcontract.Common;
using Smartcontract.Constants;
using Smartcontract.DataContracts.Profile;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities;
using Common.DataContracts.Base;
using Smartcontract.DataContracts;
using Smartcontract.DataContracts.Registration;
using Smartcontract.App.Infrastructure.Services.Abstraction;
using Smartcontract.App.Managers.Models;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;

namespace Smartcontract.App.Managers {
	public class ProfileManager {
		private readonly Provider _provider;     

		public ProfileManager(Provider provider) {
			_provider = provider;            
        }

		public async Task CreateOrAttachToProfile(ProfileCreateRequest request,bool invitedOrganization, string userName, Repository baseRepository,
            IEmailService emailService=null) {
			var personsRep = new Repository<PersonProfile>(baseRepository);
			var contragentRep = new Repository<Contragent>(baseRepository);
			var userRepository = new Repository<User>(personsRep);
            var notificaitonSettingsRep = new Repository<NotificationSettings>(baseRepository);
			var user = await userRepository.Get(x => x.UserName == userName).SingleAsync();
			var personQuery = personsRep.Get(x => x.Iin == request.PersonIin);
			if (!string.IsNullOrEmpty(request.OrganizationXin)) {
				personQuery = personQuery.Where(x => x.Contragent.Xin == request.OrganizationXin && x.Contragent.Type != ContragentTypeEnum.Individual);
			}

			var personProfile = await personQuery.FirstOrDefaultAsync();
			if (personProfile == null) {
				personProfile = new PersonProfile() {
					Iin = request.PersonIin,
					FirstName = request.PersonFirstName,
					MiddleName = request.PersonSecondName,
					LastName = request.PersonLastName,
					FullName = $"{request.PersonLastName} {request.PersonFirstName} {request.PersonSecondName}".Trim(),
					EmployeePosition = user.DefaultProfile == null ? EmployeePositionEnum.FirstHead : EmployeePositionEnum.Employee
				};
				ContragentTypeEnum contragentType;
				string contragentXin;

				if (!string.IsNullOrEmpty(request.OrganizationXin)) {
					contragentXin = request.OrganizationXin;
					if (request.OrganizationXin == request.PersonIin) {
						contragentType = ContragentTypeEnum.IndividualEnterpreneur;
					}
					else {
						contragentType = ContragentTypeEnum.Legal;
					}
				}
				else {
					contragentXin = request.PersonIin;
					contragentType = ContragentTypeEnum.Individual;
				}
				var contragent = await contragentRep.Get(x => x.Xin == contragentXin && x.Type == contragentType).SingleOrDefaultAsync();
				if (contragent == null) {
					contragent = new Contragent() {
						Xin = contragentXin,
						FullName = !string.IsNullOrEmpty(request.OrganizationFullName) ? request.OrganizationFullName : personProfile.FullName,
						Type = contragentType
					};
					contragent.UniqueId = GenerateContragentUniqueId(contragent);
					await contragentRep.InsertAsync(contragent);
				}else if (invitedOrganization) {
					contragent.FullName = !string.IsNullOrEmpty(request.OrganizationFullName) ? request.OrganizationFullName : personProfile.FullName;
				}

				personProfile.Contragent = contragent;
				await personsRep.InsertAsync(personProfile);
			}
			else {
				var existInCurrentUser = await userRepository.Get(x => x.Id == user.Id)
					.SelectMany(x => x.PersonProfiles)
					.Where(x => x.Id == personProfile.Id).Select(x => new { x.Id })
					.SingleOrDefaultAsync();
				if (existInCurrentUser != null) {
					throw new SmartcontractException($"Профиль с ИИН {request.PersonIin} уже зарегистрирован для текущего пользователя", ApiErrorCode.ValidationError);
				}
			}
			user.PersonProfiles.Add(personProfile);
			if (user.DefaultProfile == null) {
				user.DefaultProfile = personProfile;
			}

            if (emailService != null) {
                var bin = personProfile.Contragent.Xin;
                var organizationName = personProfile.Contragent.FullName;
                var text = $"Наименование организации:{organizationName}, БИН:{bin},";
                if (personProfile.Contragent.Type == ContragentTypeEnum.Individual) {
                    text = "";
                }

                var notificationSettingsUser = await notificaitonSettingsRep.Get(x => x.User.Email == userName).SingleAsync();
                var message = "Вы успешно добавили профиль в системе Smartcontract.kz Данные добавленного профиля:" +
                              $"{text} ФИО:{personProfile.FullName} , ИИН: {personProfile.Iin}";
                if (notificationSettingsUser.ProfileAdd) {
                    emailService.SendEmail(userName, "Добавлен профиль на Smartcontract.kz", message);
                }                
            }

            await userRepository.UpdateAsync(user);
            await personsRep.CommitAsync();
		}


		public async Task CreateContraget(InviteRequest request) {
			using (var rep = new Repository<Contragent>(_provider)) {
				var contragent = rep.Get(u => u.Xin == request.InviteClientRequest.Xin && u.Type==request.InviteClientRequest.ContragentType).FirstOrDefault();
				if (contragent != null) {
					throw new SmartcontractException("Пользователь с таким сочетанием ИИН/БИН и email уже существует в системе");
				}
				contragent = new Contragent() {
					Type = request.InviteClientRequest.ContragentType,
					Xin = request.InviteClientRequest.Xin,
					FullName = request.InviteClientRequest.FullName,
				};
				contragent.UniqueId = GenerateContragentUniqueId(contragent);
				rep.Insert(contragent);
				rep.Commit();
			}
		}
		private string GenerateContragentUniqueId(Contragent contragent) {
			var random = Guid.NewGuid().ToString("N").Substring(0, 5);
			return $"{contragent.Xin}_{(int)contragent.Type}_{random}";
		}

		public async Task<ProfileResponse[]> GetProfiles(string userName) {
			using (var profileRepository = new Repository<PersonProfile>(_provider)) {
				var userRepository = new Repository<User>(profileRepository);
				var user = await userRepository.Get(x => x.UserName == userName).Select(x => new { x.Id, DefaultProfileId = x.DefaultProfile.Id }).SingleAsync();
				var profiles = await userRepository.Get(x => x.Id == user.Id)
					.SelectMany(x => x.PersonProfiles)
				.Select(x => new ProfileResponse {
					Xin = x.Iin,
					FirstName = x.FirstName,
					LastName = x.LastName,
					SecondName = x.MiddleName,
					FullName = x.FullName,
                    Phone=x.Phone,
                    City=x.City,                   
                    Address=x.Address,          
					UniqueId = x.UniqueId,
					ContragentXin = x.Contragent.Xin,
					ContragentFullName = x.Contragent.FullName,
					ContragentType = x.Contragent.Type,
					IsDefault = x.Id == user.DefaultProfileId
				}).ToListAsync();
				return profiles.ToArray();
			}
		}

		public async Task<Guid> RemoveProfile(Guid personUniqueId, string userName,IEmailService emailService) {
			using (var profileImplRepository = new Repository<PersonProfile>(_provider)) {
                var notificaitonSettingsRep = new Repository<NotificationSettings>(profileImplRepository);
				var userRepository = new Repository<User>(profileImplRepository);
				var user = await userRepository.Get(x => x.UserName == userName).SingleAsync();
				var profile = await GetPersonProfileQuery(userName, personUniqueId, profileImplRepository).FirstOrDefaultAsync();
				if (profile == null) {
					throw new SmartcontractException($"Выбранный профиль не найден", ApiErrorCode.ValidationError);
				}

				if (user.PersonProfiles.Count == 1) {
					throw new SmartcontractException($"Невозможно удалить единственный профиль", ApiErrorCode.ValidationError);
				}

				if (user.DefaultProfile == profile) {
					var otherProfile = await profileImplRepository.Get(x => x.Users.Any(u => u.Id == user.Id) && x.Id != profile.Id).FirstAsync();
					user.DefaultProfile = otherProfile;
					await userRepository.UpdateAsync(user);
				}

				user.PersonProfiles.Remove(profile);                                 
				await userRepository.UpdateAsync(user);
				await profileImplRepository.CommitAsync();
                var bin = profile.Contragent.Xin;
                var organizationName = profile.Contragent.FullName;                
                var text = $"Наименование организации:{organizationName}, БИН:{bin},";
                if (profile.Contragent.Type == ContragentTypeEnum.Individual) {
                    text = "";
                }

                var notificationSettingsUser = await notificaitonSettingsRep.Get(x => x.User == user).SingleAsync();
                var message = "Вы успешно удалили профиль в системе Smartcontract.kz Данные удаленного профиля:" +
                              $"{text} ФИО:{profile.FullName} , ИИН: {profile.Iin}";
                if (notificationSettingsUser.ProfileRemove) {
                    emailService.SendEmail(user.Email, "Вы удалили профиль", message);
                }                
                return user.DefaultProfile.UniqueId;
            }
		}

        public async Task ChangeProfile(ProfileChangeRequest request) {
            using (var ProfileRep = new Repository<PersonProfile>(_provider)) {
                var profile = await ProfileRep.Get(x => x.UniqueId == new Guid(request.UniqueId)).SingleAsync();
                profile.Phone = request.Phone;
                profile.Address = request.Address;
                profile.City = request.City;
                var avatarRep = new Repository<ProfileAvatar>(ProfileRep);
                var profileAvatar = await avatarRep.Get(x => x.Profile == profile).FirstOrDefaultAsync();
                var isExistAvatar=true;
                if (profileAvatar == null) {
                    profileAvatar = new ProfileAvatar();
                    isExistAvatar=false;
                }

                if (request.Avatar != null) {
                    using (var memoryStream = new MemoryStream())
                    {
                        await request.Avatar.CopyToAsync(memoryStream);                    
                        profileAvatar.Avatar = memoryStream.ToArray();
                        profileAvatar.ContentType = request.Avatar.ContentType;
                        profileAvatar.FileName = request.Avatar.FileName;
                        profileAvatar.Profile = profile;                    
                    }
                    if (!isExistAvatar) {
                        await avatarRep.InsertAsync(profileAvatar);
                    }
                    else {
                        await avatarRep.UpdateAsync(profileAvatar);
                    }
                }                                
                ProfileRep.Update(profile);
                ProfileRep.Commit();
            }
        }

        public async Task<ProfileAvatar> GetAvatar(Guid uniqueId) {
            using (var ProfileRep = new Repository<PersonProfile>(_provider)) {
                var profile = await ProfileRep.Get(x => x.UniqueId ==uniqueId).SingleAsync();
                var avatarRep = new Repository<ProfileAvatar>(ProfileRep);
                var avatar= await avatarRep.Get(x => x.Profile == profile).SingleAsync();
                return avatar;
            }
        }

		public async Task<IQueryable<RecipientProfile>> GetRecipientsAsync(Guid personProfileUniqueId, string userName, Repository repository) {
			var currentContragentId = await GetPersonProfileQuery(userName, personProfileUniqueId, repository).Select(x => x.Contragent.Id).FirstOrDefaultAsync();
			var contragents = new Repository<Contragent>(repository).Get(x => x.Id != currentContragentId)
				.Select(x => new RecipientProfile {
					UniqueId = x.UniqueId,
					FullName = x.FullName
				});
			return contragents;
		}

		public IQueryable<PersonProfile> GetPersonProfileQuery(string userName, Guid personProfileUniqueId, Repository rep) {
			var userRep = new Repository<User>(rep);
			return userRep
				.Get(x => x.UserName == userName)
				.SelectMany(x => x.PersonProfiles)
				.Where(x => x.UniqueId == personProfileUniqueId)
				.WithOptions(x=>x.SetCacheable(true));
		}
	}
}
