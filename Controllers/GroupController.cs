using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System;
using dotnet_Learn.Data;
using dotnet_Learn.Entities;

namespace dotnet_Learn.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class GroupController : ControllerBase
    {
        private readonly IMongoCollection<Group> _groups;
        private readonly IMongoCollection<Message> _messages;
        private readonly IMongoCollection<User> _users;
        private readonly IHubContext<Hubs.ChatHub> _hubContext;

        public GroupController(MongoDbService mongoDbService, IHubContext<Hubs.ChatHub> hubContext)
        {
            _groups = mongoDbService.database.GetCollection<Group>("Groups");
            _messages = mongoDbService.database.GetCollection<Message>("Messages");
            _users = mongoDbService.database.GetCollection<User>("Users");
            _hubContext = hubContext;
        }

        // Create a new group
        [HttpPost]
        public async Task<IActionResult> CreateGroup([FromBody] CreateGroupDto dto)
        {
            var myId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (string.IsNullOrEmpty(myId)) return Unauthorized();

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("Group name is required");

            var memberIds = dto.MemberIds ?? new List<string>();
            if (!memberIds.Contains(myId))
            {
                memberIds.Add(myId);
            }

            var group = new Group
            {
                Name = dto.Name.Trim(),
                MemberIds = memberIds,
                CreatedBy = myId,
                CreatedAt = DateTime.UtcNow
            };

            await _groups.InsertOneAsync(group);

            // Dynamically add all connected group members to the SignalR group
            foreach (var memberId in memberIds)
            {
                if (Hubs.ChatHub._userConnections.TryGetValue(memberId, out var connections))
                {
                    foreach (var connectionId in connections)
                    {
                        try
                        {
                            await _hubContext.Groups.AddToGroupAsync(connectionId, group.Id);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error adding connection {connectionId} to SignalR group {group.Id}: {ex.Message}");
                        }
                    }
                }
            }

            // Notify all group members that a new group has been created
            await _hubContext.Clients.Groups(group.Id).SendAsync("GroupCreated", group);

            return Ok(group);
        }

        // Get all groups the current user belongs to
        [HttpGet]
        public async Task<IActionResult> GetMyGroups()
        {
            var myId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (string.IsNullOrEmpty(myId)) return Unauthorized();

            var filter = Builders<Group>.Filter.AnyEq(g => g.MemberIds, myId);
            var groups = await _groups.Find(filter).ToListAsync();

            return Ok(groups);
        }

        // Get messages for a specific group
        [HttpGet("{groupId}/messages")]
        public async Task<IActionResult> GetGroupMessages(string groupId)
        {
            var myId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (string.IsNullOrEmpty(myId)) return Unauthorized();

            // Verify membership
            var group = await _groups.Find(g => g.Id == groupId).FirstOrDefaultAsync();
            if (group == null) return NotFound("Group not found");
            if (!group.MemberIds.Contains(myId)) return Forbid("You are not a member of this group");

            var filter = Builders<Message>.Filter.Eq(m => m.GroupId, groupId);
            var messages = await _messages.Find(filter)
                                          .SortBy(m => m.Timestamp)
                                          .ToListAsync();

            return Ok(messages);
        }

        // Send a message to a group
        [HttpPost("{groupId}/message")]
        public async Task<IActionResult> SendGroupMessage(string groupId, [FromBody] SendGroupMessageDto dto)
        {
            var myId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (string.IsNullOrEmpty(myId)) return Unauthorized();

            // Verify membership
            var group = await _groups.Find(g => g.Id == groupId).FirstOrDefaultAsync();
            if (group == null) return NotFound("Group not found");
            if (!group.MemberIds.Contains(myId)) return Forbid("You are not a member of this group");

            var message = new Message
            {
                SenderId = myId,
                GroupId = groupId,
                Text = dto.Text,
                Status = "Sent" // For groups, message status is marked sent/delivered differently
            };

            await _messages.InsertOneAsync(message);

            // 1. Broadcast via SignalR to group
            await _hubContext.Clients.Group(groupId).SendAsync("ReceiveGroupMessage", message);

            // 2. Send push notifications to offline/other members in the group
            var otherMemberIds = group.MemberIds.Where(id => id != myId).ToList();
            if (otherMemberIds.Any())
            {
                try
                {
                    var sender = await _users.Find(u => u.Id == myId).FirstOrDefaultAsync();
                    var senderName = sender?.Name ?? "Group Member";
                    
                    Console.WriteLine($"--- GROUP FCM LOG: Checking group members for push notifications in group '{group.Name}' ---");
                    var offlineUsers = await _users.Find(u => otherMemberIds.Contains(u.Id)).ToListAsync();
                    
                    foreach (var user in offlineUsers)
                    {
                        // Check if they are actually offline in SignalR (to avoid double notification)
                        bool isOnline = Hubs.ChatHub._userConnections.ContainsKey(user.Id);
                        
                        if (string.IsNullOrEmpty(user.FcmToken))
                        {
                            Console.WriteLine($"--- GROUP FCM LOG: Member '{user.Name}' has NULL or EMPTY FcmToken in MongoDB. Skipping. ---");
                            continue;
                        }
                        
                        if (isOnline)
                        {
                            Console.WriteLine($"--- GROUP FCM LOG: Member '{user.Name}' is currently ONLINE via SignalR connection. Skipping push. ---");
                            continue;
                        }

                        try
                        {
                            Console.WriteLine($"--- GROUP FCM LOG: Sending group push notification to '{user.Name}' (Token: '{user.FcmToken.Substring(0, Math.Min(15, user.FcmToken.Length))}...') ---");
                            var notificationMessage = new FirebaseAdmin.Messaging.Message()
                            {
                                Token = user.FcmToken,
                                Notification = new FirebaseAdmin.Messaging.Notification()
                                {
                                    Title = $"{group.Name} - {senderName}",
                                    Body = dto.Text
                                },
                                Android = new FirebaseAdmin.Messaging.AndroidConfig()
                                {
                                    Priority = FirebaseAdmin.Messaging.Priority.High,
                                    Notification = new FirebaseAdmin.Messaging.AndroidNotification()
                                    {
                                        Sound = "default",
                                        ClickAction = "FLUTTER_NOTIFICATION_CLICK",
                                        ChannelId = "high_importance_channel"
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
                                    { "groupId", groupId },
                                    { "groupName", group.Name },
                                    { "type", "group" }
                                }
                            };

                            if (FirebaseAdmin.FirebaseApp.DefaultInstance == null)
                            {
                                Console.WriteLine("--- GROUP FCM LOG: Cannot send notification because FirebaseApp has NOT been initialized! Please configure the FIREBASE_SERVICE_ACCOUNT_JSON environment variable or service-account.json. ---");
                            }
                            else
                            {
                                var fcmResponse = await FirebaseAdmin.Messaging.FirebaseMessaging.DefaultInstance.SendAsync(notificationMessage);
                                Console.WriteLine($"--- GROUP FCM LOG: Successfully sent notification to '{user.Name}'. Response ID: {fcmResponse} ---");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"--- GROUP FCM EXCEPTION for user {user.Id}: {ex.Message} ---");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"--- GROUP FCM GENERAL EXCEPTION: {ex.Message} ---");
                }
            }

            return Ok(message);
        }
    }

    public class CreateGroupDto
    {
        public string Name { get; set; } = string.Empty;
        public List<string> MemberIds { get; set; } = new List<string>();
    }

    public class SendGroupMessageDto
    {
        public string Text { get; set; } = string.Empty;
    }
}
