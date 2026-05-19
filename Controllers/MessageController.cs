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
        private readonly IMongoCollection<User> _users;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<Hubs.ChatHub> _hubContext;

        public MessageController(MongoDbService mongoDbService, Microsoft.AspNetCore.SignalR.IHubContext<Hubs.ChatHub> hubContext)
        {
            _messages = mongoDbService.database.GetCollection<Message>("Messages");
            _users = mongoDbService.database.GetCollection<User>("Users");
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

            // 1. Notify via SignalR (Instant if app is open)
            await _hubContext.Clients.Users(request.ReceiverId, senderId).SendAsync("ReceiveMessage", message);

            // 2. Notify via Push Notification (If app is closed)
            try
            {
                var receiver = await _users.Find(u => u.Id == request.ReceiverId).FirstOrDefaultAsync();
                if (receiver == null)
                {
                    Console.WriteLine($"--- FCM PUSH LOG: Receiver with ID '{request.ReceiverId}' NOT found in MongoDB! ---");
                }
                else if (string.IsNullOrEmpty(receiver.FcmToken))
                {
                    Console.WriteLine($"--- FCM PUSH LOG: Receiver '{receiver.Name}' has NULL or EMPTY FcmToken in MongoDB! ---");
                }
                else
                {
                    Console.WriteLine($"--- FCM PUSH LOG: Sending FCM notification to receiver '{receiver.Name}' with Token: '{receiver.FcmToken.Substring(0, Math.Min(15, receiver.FcmToken.Length))}...' ---");
                    var sender = await _users.Find(u => u.Id == senderId).FirstOrDefaultAsync();
                    var notificationMessage = new FirebaseAdmin.Messaging.Message()
                    {
                        Token = receiver.FcmToken,
                        Notification = new FirebaseAdmin.Messaging.Notification()
                        {
                            Title = sender?.Name ?? "New Message",
                            Body = request.Text
                        },
                        Android = new FirebaseAdmin.Messaging.AndroidConfig()
                        {
                            Priority = FirebaseAdmin.Messaging.Priority.High,
                            Notification = new FirebaseAdmin.Messaging.AndroidNotification()
                            {
                                Sound = "default",
                                ClickAction = "FLUTTER_NOTIFICATION_CLICK"
                            }
                        },
                        Apns = new FirebaseAdmin.Messaging.ApnsConfig()
                        {
                            Headers = new Dictionary<string, string>()
                            {
                                { "apns-priority", "10" }
                            }
                        },
                        Data = new Dictionary<string, string>()
                        {
                            { "senderId", senderId },
                            { "type", "chat" }
                        }
                    };

                    if (FirebaseAdmin.FirebaseApp.DefaultInstance == null)
                    {
                        Console.WriteLine("--- FCM PUSH LOG: Cannot send notification because FirebaseApp has NOT been initialized! Please configure the FIREBASE_SERVICE_ACCOUNT_JSON environment variable in your Render dashboard, or place the service-account.json file in the backend root. ---");
                    }
                    else
                    {
                        var fcmResponse = await FirebaseAdmin.Messaging.FirebaseMessaging.DefaultInstance.SendAsync(notificationMessage);
                        Console.WriteLine($"--- FCM PUSH LOG: Firebase Admin successfully sent message. Response ID: {fcmResponse} ---");
                    }
                }
            }
            catch (Exception ex)
            {
                // Log notification error but don't fail the message send
                Console.WriteLine($"--- FCM PUSH EXCEPTION: {ex.Message} ---\n{ex.StackTrace}");
            }

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

        [HttpPost("read/{senderId}")]
        public async Task<IActionResult> MarkAsRead(string senderId)
        {
            var myId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (string.IsNullOrEmpty(myId)) return Unauthorized();

            var filter = Builders<Message>.Filter.And(
                Builders<Message>.Filter.Eq(m => m.SenderId, senderId),
                Builders<Message>.Filter.Eq(m => m.ReceiverId, myId),
                Builders<Message>.Filter.Ne(m => m.Status, "Read")
            );

            var update = Builders<Message>.Update.Set(m => m.Status, "Read");
            await _messages.UpdateManyAsync(filter, update);

            // Notify the sender that their messages were read!
            await _hubContext.Clients.User(senderId).SendAsync("MessagesRead", myId);

            return Ok();
        }
    }

    public class SendMessageDto
    {
        public string ReceiverId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
