using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using MongoDB.Driver;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using dotnet_Learn.Data;
using dotnet_Learn.Entities;

namespace dotnet_Learn.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IMongoCollection<User> _users;
        private readonly IMongoCollection<Group> _groups;
        // Static dictionary to track connection IDs for each user
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>> _userConnections = 
            new System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>>();

        public ChatHub(MongoDbService mongoDbService)
        {
            _users = mongoDbService.database.GetCollection<User>("Users");
            _groups = mongoDbService.database.GetCollection<Group>("Groups");
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) 
                         ?? Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub);

            Console.WriteLine($"--- SignalR Connection: {Context.ConnectionId} | User: {userId ?? "ANONYMOUS"} ---");

            if (!string.IsNullOrEmpty(userId))
            {
                var connections = _userConnections.GetOrAdd(userId, _ => new HashSet<string>());
                lock (connections)
                {
                    connections.Add(Context.ConnectionId);
                }

                if (connections.Count == 1)
                {
                    await _users.UpdateOneAsync(u => u.Id == userId, Builders<User>.Update.Set(u => u.IsOnline, true));
                    await Clients.All.SendAsync("UserStatusChanged", userId, true);
                    Console.WriteLine($"--- User {userId} is now ONLINE ---");
                }

                // Join all SignalR groups this user is a member of
                try
                {
                    var userGroups = await _groups.Find(g => g.MemberIds.Contains(userId)).ToListAsync();
                    foreach (var group in userGroups)
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, group.Id);
                        Console.WriteLine($"--- Added connection {Context.ConnectionId} of User {userId} to SignalR Group {group.Id} ---");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error joining groups in OnConnectedAsync: {ex.Message}");
                }
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) 
                         ?? Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub);

            Console.WriteLine($"--- SignalR Disconnection: {Context.ConnectionId} | User: {userId ?? "ANONYMOUS"} ---");
// ...

            if (!string.IsNullOrEmpty(userId))
            {
                if (_userConnections.TryGetValue(userId, out var connections))
                {
                    bool isLastConnection = false;
                    lock (connections)
                    {
                        connections.Remove(Context.ConnectionId);
                        if (connections.Count == 0)
                        {
                            isLastConnection = true;
                            _userConnections.TryRemove(userId, out _);
                        }
                    }

                    // Only update DB and notify others if this was the LAST connection for this user
                    if (isLastConnection)
                    {
                        await _users.UpdateOneAsync(u => u.Id == userId, Builders<User>.Update.Set(u => u.IsOnline, false).Set(u => u.LastSeen, DateTime.UtcNow));
                        await Clients.All.SendAsync("UserStatusChanged", userId, false);
                        Console.WriteLine($"--- User {userId} is now OFFLINE ---");
                    }
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        // Method for sending messages through WebSockets
        public async Task SendMessageToUser(string receiverId, string messageText)
        {
            var senderId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(senderId)) return;

            // We notify the receiver and sender (if online)
            await Clients.Users(receiverId, senderId).SendAsync("ReceiveMessage", senderId, messageText);
        }

        // Method to broadcast typing status changes
        public async Task SendTyping(string receiverId, bool isTyping)
        {
            var senderId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(senderId)) return;

            // Broadcast to the target user that senderId is typing
            await Clients.User(receiverId).SendAsync("UserTyping", senderId, isTyping);
        }
    }
}
