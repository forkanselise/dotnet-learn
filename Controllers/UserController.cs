using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using dotnet_Learn.Entities;
using dotnet_Learn.Data;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace dotnet_Learn.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class UserController : ControllerBase
    {
        //private readonly MongoDbService _mongoDbService;
        private readonly IMongoCollection<User>? _users;
        public UserController(MongoDbService mongoDbService)
        {
            _users = mongoDbService.database.GetCollection<User>("Users");
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var users = await _users.Find(FilterDefinition<User>.Empty).ToListAsync();
            var safeUsers = users.Select(u => new {
                u.Id,
                u.Name,
                u.Email,
                u.IsOnline,
                u.LastSeen
            });
            return Ok(safeUsers);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult> GetUserById(string id)
        {
            var user = await _users.Find(u => u.Id == id).FirstOrDefaultAsync();
            if (user == null)
            {
                return NotFound();
            }
            return Ok(new {
                user.Id,
                user.Name,
                user.Email,
                user.IsOnline,
                user.LastSeen
            });
        }

        [HttpPost]
        public async Task<ActionResult<User>> CreateUser(User user)
        {
            await _users.InsertOneAsync(user);
            return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, user);
        }

        [HttpPut]
        public async Task<ActionResult> UpdateUser(string id, User updatedUser)
        {
            var result = await _users.ReplaceOneAsync(u => u.Id == id, updatedUser);
            if (result.MatchedCount == 0)
            {
                return NotFound();
            }
            return NoContent();
        }

        [HttpDelete]
        public async Task<ActionResult> DeleteUser(string id)
        {
            var result = await _users.DeleteOneAsync(u => u.Id == id);
            if (result.DeletedCount == 0)
            {
                return NotFound();
            }
            return NoContent();
        }

        [HttpPut("fcm-token")]
        public async Task<IActionResult> UpdateFcmToken([FromBody] FcmTokenDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var update = Builders<User>.Update.Set(u => u.FcmToken, dto.Token);
            await _users.UpdateOneAsync(u => u.Id == userId, update);

            return Ok();
        }

        public class FcmTokenDto
        {
            public string Token { get; set; } = string.Empty;
        }
    }
}

