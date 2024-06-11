using System;
using System.Collections.Generic;
using System.Linq;
using TelegramClient.Models;
using TL;

namespace TelegramClient.Factory.FactoriesUsers
{

    public class User : Base.UserBase
    {
        public User(IList<long> listenToChannel) : base(listenToChannel)
        {

        }

        public override ChatDto Execute(UpdatesBase updates)
        {
            try
            {
                if (updates.Users.Count == 0)
                    return null;

                var user = ((Updates)updates).Users?.FirstOrDefault(u => listenToChannel.Contains(u.Key));
                var username = user.Value.Value?.ToString().Replace("@", "");
                if (username == null) return null;

                return new ChatDto()
                {
                    Id = user.Value.Value.ID,
                    Name = $"{user.Value.Value.first_name} {user.Value.Value.last_name}",
                    Username = username
                };
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
