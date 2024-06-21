﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Models;
using TL;
using WTelegram;

namespace TelegramClient.Factory.Interfaces.Messages
{
    interface IMessageType
    {
        public Client Client { get; }
        public MessageTypes TypeMessage { get; set; }
        public Task<bool> ExecuteAsync(Message message, ChatDto chatDto);

    }
}
