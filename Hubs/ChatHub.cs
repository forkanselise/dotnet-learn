using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using dotnet_Learn.Data;
using dotnet_Learn.Entities;

namespace dotnet_Learn.Hubs
{
    public class ChatHub : Hub
    {
        private readonly IMongoCollection<User> _users;

        public ChatHub(MongoDbService mongoDbService)
        {
            _users = mongoDbService.database.GetCollection<User>("Users");
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) 
                         ?? Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub);

            if (!string.IsNullOrEmpty(userId))
            {
                var update = Builders<User>.Update
                    .Set(u => u.IsOnline, true)
                    .Set(u => u.LastSeen, DateTime.UtcNow);
                
                await _users.UpdateOneAsync(u => u.Id == userId, update);
                
                // Notify everyone that this user is online
                await Clients.All.SendAsync("UserStatusChanged", userId, true);
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) 
                         ?? Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub);

            if (!string.IsNullOrEmpty(userId))
            {
                var update = Builders<User>.Update
                    .Set(u => u.IsOnline, false)
                    .Set(u => u.LastSeen, DateTime.UtcNow);
                
                await _users.UpdateOneAsync(u => u.Id == userId, update);
                
                // Notify everyone that this user is offline
                await Clients.All.SendAsync("UserStatusChanged", userId, false);
            }

            await base.OnDisconnectedAsync(exception);
        }

        // Method for sending messages through WebSockets
        public async Task SendMessageToUser(string receiverId, string messageText)
        {
            var senderId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) 
                           ?? Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub);

            if (string.IsNullOrEmpty(senderId)) return;

            // We notify the receiver (if they are online)
            await Clients.User(receiverId).SendAsync("ReceiveMessage", senderId, messageText);
        }
    }
}
