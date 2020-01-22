using Smartcontract.DataContracts.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Smartcontract.App.Infrastructure.Services.Abstraction
{
    public interface ISettingsManager {
          Task SaveNotificationSettings(string email, NotificationSettingsRequest request);
          Task<NotificationSettingsResponse> GetNotificationSettings(string email);
    }
}
