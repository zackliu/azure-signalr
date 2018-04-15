// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.SignalR;

namespace ChatSample
{
    [Route("health")]
    public class HealthController : Controller
    {
        private readonly IMessageCounters _messageCounters;

        public HealthController(IMessageCounters messageCounters)
        {
            _messageCounters = messageCounters;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return new OkObjectResult(
                new
                {
                    IncomingMessage = _messageCounters.GetIncomingMessageCount(),
                    OutgoingMessage = _messageCounters.GetOutgoingMessageCount()
                });
        }
    }
}