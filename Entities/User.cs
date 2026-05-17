using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace dotnet_Learn.Entities
{
    public class User
    {
        [BsonId]
        [BsonElement("_id"), BsonRepresentation(BsonType.String)]
        public string? Id { get; set; } = System.Guid.NewGuid().ToString();

        [BsonElement("name"), BsonRepresentation(BsonType.String)]
        public string? Name { get; set; }
        [BsonElement("email"), BsonRepresentation(BsonType.String)]
        public string? Email { get; set; }
        [BsonElement("passwordHash"), BsonRepresentation(BsonType.String)]
        public string? PasswordHash { get; set; }

        [BsonElement("isOnline"), BsonRepresentation(BsonType.Boolean)]
        public bool IsOnline { get; set; } = false;
        
        [BsonElement("fcmToken"), BsonRepresentation(BsonType.String)]
        public string? FcmToken { get; set; }

        [BsonElement("lastSeen"), BsonRepresentation(BsonType.DateTime)]
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    }
}
