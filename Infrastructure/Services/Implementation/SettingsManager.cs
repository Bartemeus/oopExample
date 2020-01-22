using Common.DAL.Abstraction.Repositories;
using Smartcontract.App.Infrastructure.Services.Abstraction;
using Smartcontract.DAL;
using Smartcontract.DAL.Entities;
using Smartcontract.DataContracts;
using Smartcontract.DataContracts.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Smartcontract.App.Infrastructure.Services.Implementation
{
    public class SettingsManager:ISettingsManager
    {
        private readonly Provider _provider;

        public SettingsManager(Provider provider) {
            _provider = provider;
        }

        public async Task SaveNotificationSettings(string email,NotificationSettingsRequest request) {
            using (var userRepository = new Repository<User>(_provider)) {
                var user = userRepository.Get(x => x.Email == email).Single();                
                var notificationSettingsRep = new Repository<NotificationSettings>(userRepository);
                var notificationSettings = notificationSettingsRep.Get(x => x.User == user).Single();                
                notificationSettings.InviteSend = request.InviteSend;
                notificationSettings.ProfileAdd = request.ProfileAdd;
                notificationSettings.ProfileRemove = request.ProfileRemove;
                notificationSettings.DocumentReceived = request.DocumentReceived;
                notificationSettings.DocumentRejected = request.DocumentRejected;
                notificationSettings.DocumentRetired = request.DocumentRetired;
                notificationSettings.DocumentSend = request.DocumentSend;
                notificationSettings.DocumentSign = request.DocumentSign;
                await notificationSettingsRep.UpdateAsync(notificationSettings);                               
                await notificationSettingsRep.CommitAsync();
            }
        }

        public async Task<NotificationSettingsResponse> GetNotificationSettings(string email) {
            using (var userRep = new Repository<User>(_provider)) {
                var user = userRep.Get(x => x.Email == email).Single();
                var notificationSettingsRep = new Repository<NotificationSettings>(userRep);
                var notificationSettings = notificationSettingsRep.Get(x => x.User == user).Single();
                return new NotificationSettingsResponse {
                    DocumentReceived = notificationSettings.DocumentReceived,
                    DocumentRejected = notificationSettings.DocumentRejected,
                    DocumentRetired = notificationSettings.DocumentRetired,
                    DocumentSend = notificationSettings.DocumentSend,
                    DocumentSign=notificationSettings.DocumentSign,
                    ProfileAdd = notificationSettings.ProfileAdd,
                    InviteSend = notificationSettings.InviteSend,
                    ProfileRemove = notificationSettings.ProfileRemove
                };
            }
        }
    }
}
