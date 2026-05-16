using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using dotnet_Learn.Data;
using dotnet_Learn.Entities;

namespace dotnet_Learn.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class MessageController : ControllerBase
    {
        private readonly IMongoCollection<Message> _messages;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<Hubs.ChatHub> _hubContext;

        public MessageController(MongoDbService mongoDbService, Microsoft.AspNetCore.SignalR.IHubContext<Hubs.ChatHub> hubContext)
        {
            _messages = mongoDbService.database.GetCollection<Message>("Messages");
            _hubContext = hubContext;
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageDto request)
        {
            var senderId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            
            if (string.IsNullOrEmpty(senderId))
                return Unauthorized();

            var message = new Message
            {
                SenderId = senderId,
                ReceiverId = request.ReceiverId,
                Text = request.Text
            };

            await _messages.InsertOneAsync(message);

            // Notify both Receiver and Sender via SignalR!
            await _hubContext.Clients.Users(request.ReceiverId, senderId).SendAsync("ReceiveMessage", senderId, request.Text);

            return Ok(message);
        }

        [HttpGet("{otherUserId}")]
        public async Task<ActionResult<IEnumerable<Message>>> GetConversation(string otherUserId)
        {
            var myId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

            if (string.IsNullOrEmpty(myId))
                return Unauthorized();

            // Find messages where I am the sender and they are the receiver, OR they are the sender and I am the receiver.
            var filter = Builders<Message>.Filter.Or(
                Builders<Message>.Filter.And(
                    Builders<Message>.Filter.Eq(m => m.SenderId, myId),
                    Builders<Message>.Filter.Eq(m => m.ReceiverId, otherUserId)
                ),
                Builders<Message>.Filter.And(
                    Builders<Message>.Filter.Eq(m => m.SenderId, otherUserId),
                    Builders<Message>.Filter.Eq(m => m.ReceiverId, myId)
                )
            );

            var messages = await _messages.Find(filter)
                                          .SortBy(m => m.Timestamp)
                                          .ToListAsync();
            return Ok(messages);
        }
    }

    public class SendMessageDto
    {
        public string ReceiverId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
